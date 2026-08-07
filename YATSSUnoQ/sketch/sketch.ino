#include <Arduino_RouterBridge.h>

const byte LaneCount = 8;
const byte SensorQueueSize = 64;
const byte SensorQueueMask = SensorQueueSize - 1;
const unsigned long HeartbeatIntervalMillis = 1000;
const unsigned long WindowsKeepaliveTimeoutMillis = 5000;
const unsigned long DiagnosticSessionTimeoutMillis = 5000;
const unsigned long MaxDiagnosticRelayPulseMillis = 2000;
const unsigned long DefaultEdgeDebounceMillis = 1800;
const unsigned long MaxEdgeDebounceMillis = 10000;
const byte TrackPowerCutActiveLevel = HIGH;
const char ControllerBoardProfile[] = "ARDUINO_UNO_Q_STM32U585";
const char FirmwareVersion[] = "0.10.0-beta.1-dev";

static_assert((SensorQueueSize & SensorQueueMask) == 0,
              "SensorQueueSize must be a power of two");

const byte sensorPins[LaneCount] = { D2, D3, D4, D5, D6, D7, D8, D9 };
const byte trackPowerCutPins[LaneCount] = { D10, D11, D12, D13, A0, A1, A2, A3 };

unsigned long laneSequences[LaneCount] = {};
unsigned long lastEdgeMillis[LaneCount] = {};
unsigned long diagnosticTransitionCounts[LaneCount] = {};
unsigned long diagnosticAcceptedEdgeCounts[LaneCount] = {};
unsigned long diagnosticLastAcceptedMillis[LaneCount] = {};
unsigned long edgeDebounceMillis = DefaultEdgeDebounceMillis;
unsigned long lastHeartbeatMillis = 0;
unsigned long lastWindowsCommandMillis = 0;
unsigned long lastDiagnosticCommandMillis = 0;
bool windowsWatchdogArmed = false;
bool diagnosticMode = false;
byte trackPowerEnabledMask = 0;
bool diagnosticRelayPulseActive = false;
byte diagnosticRelayPulseLane = 0;
byte diagnosticRelayRestoreMask = 0;
unsigned long diagnosticRelayPulseEndsMillis = 0;

struct SensorTransition {
  byte lane;
  byte active;
  unsigned long timestampMillis;
};

volatile SensorTransition sensorQueue[SensorQueueSize];
volatile byte sensorQueueHead = 0;
volatile byte sensorQueueTail = 0;
volatile unsigned long droppedSensorTransitions = 0;
volatile unsigned long totalDroppedSensorTransitions = 0;

void enqueueSensorTransition(byte lane) {
  byte nextHead = (byte)((sensorQueueHead + 1) & SensorQueueMask);
  if (nextHead == sensorQueueTail) {
    droppedSensorTransitions++;
    totalDroppedSensorTransitions++;
    return;
  }

  sensorQueue[sensorQueueHead].lane = lane;
  sensorQueue[sensorQueueHead].active = digitalRead(sensorPins[lane]) == LOW ? 1 : 0;
  sensorQueue[sensorQueueHead].timestampMillis = millis();
  sensorQueueHead = nextHead;
}

void isrLane0() { enqueueSensorTransition(0); }
void isrLane1() { enqueueSensorTransition(1); }
void isrLane2() { enqueueSensorTransition(2); }
void isrLane3() { enqueueSensorTransition(3); }
void isrLane4() { enqueueSensorTransition(4); }
void isrLane5() { enqueueSensorTransition(5); }
void isrLane6() { enqueueSensorTransition(6); }
void isrLane7() { enqueueSensorTransition(7); }

void (*isrHandlers[LaneCount])() = {
  isrLane0, isrLane1, isrLane2, isrLane3,
  isrLane4, isrLane5, isrLane6, isrLane7
};

void flushSensorQueue() {
  noInterrupts();
  sensorQueueTail = sensorQueueHead;
  interrupts();
}

byte calculateChecksum(const String &body) {
  byte checksum = 0;
  for (unsigned int index = 0; index < body.length(); index++) {
    checksum ^= (byte)body[index];
  }
  return checksum;
}

String toHexByte(byte value) {
  String result;
  if (value < 16) {
    result += '0';
  }
  result += String(value, HEX);
  result.toUpperCase();
  return result;
}

void sendFrame(const String &body) {
  byte checksum = calculateChecksum(body);
  String checksumText = String(checksum, HEX);
  checksumText.toUpperCase();
  String frame = body + F("*");
  if (checksum < 16) {
    frame += '0';
  }
  frame += checksumText;
  Bridge.notify("yatss_frame", frame);
}

bool stripAndValidateChecksum(String &line) {
  int marker = line.lastIndexOf('*');
  if (marker < 0) {
    return false;
  }
  String body = line.substring(0, marker);
  String checksumText = line.substring(marker + 1);
  if (checksumText.length() != 2) {
    return false;
  }
  char *end = nullptr;
  unsigned long expected = strtoul(checksumText.c_str(), &end, 16);
  if (end == checksumText.c_str() || *end != '\0' || expected > 255 ||
      calculateChecksum(body) != (byte)expected) {
    return false;
  }
  line = body;
  return true;
}

void setTrackPowerMask(byte enabledLaneMask) {
  trackPowerEnabledMask = enabledLaneMask;
  byte restoreLevel = TrackPowerCutActiveLevel == HIGH ? LOW : HIGH;
  for (byte lane = 0; lane < LaneCount; lane++) {
    bool enabled = (enabledLaneMask & (1 << lane)) != 0;
    digitalWrite(trackPowerCutPins[lane], enabled ? restoreLevel : TrackPowerCutActiveLevel);
  }
}

byte readActiveSensorMask() {
  byte mask = 0;
  for (byte lane = 0; lane < LaneCount; lane++) {
    if (digitalRead(sensorPins[lane]) == LOW) {
      mask |= (byte)(1 << lane);
    }
  }
  return mask;
}

void sendControllerHello() {
  sendFrame(String(F("HELLO:YATSSMC:4:")) + LaneCount + ":" +
            ControllerBoardProfile + ":" + FirmwareVersion + F(":2097152"));
}

void sendDiagnosticStatus() {
  unsigned long dropped;
  noInterrupts();
  dropped = totalDroppedSensorTransitions;
  interrupts();
  sendFrame(String(F("DIAG:STATUS:")) + toHexByte(readActiveSensorMask()) + F(":") +
            toHexByte(trackPowerEnabledMask) + F(":") + edgeDebounceMillis +
            F(":") + dropped + F(":") + millis());
}

void stopDiagnosticSession(const char *reason) {
  if (diagnosticRelayPulseActive) {
    setTrackPowerMask(diagnosticRelayRestoreMask);
    diagnosticRelayPulseActive = false;
  }
  diagnosticMode = false;
  flushSensorQueue();
  for (byte lane = 0; lane < LaneCount; lane++) {
    lastEdgeMillis[lane] = 0;
  }
  sendFrame(String(F("DIAG:SESSION:STOPPED:")) + reason + F(":") + millis());
}

void startDiagnosticSession() {
  flushSensorQueue();
  diagnosticMode = true;
  for (byte lane = 0; lane < LaneCount; lane++) {
    diagnosticTransitionCounts[lane] = 0;
    diagnosticAcceptedEdgeCounts[lane] = 0;
    diagnosticLastAcceptedMillis[lane] = 0;
  }
  lastDiagnosticCommandMillis = millis();
  sendFrame(String(F("DIAG:SESSION:STARTED:")) + lastDiagnosticCommandMillis);
  sendDiagnosticStatus();
}

void handleRelayPulse(const String &arguments) {
  if (!diagnosticMode || diagnosticRelayPulseActive) {
    sendFrame(diagnosticMode ? F("ERR:DIAG_RELAY_BUSY") : F("ERR:DIAG_NOT_ACTIVE"));
    return;
  }
  int separator = arguments.indexOf(':');
  if (separator <= 0) {
    sendFrame(F("ERR:BAD_DIAG_RELAY"));
    return;
  }
  String laneText = arguments.substring(0, separator);
  String durationText = arguments.substring(separator + 1);
  char *laneEnd = nullptr;
  char *durationEnd = nullptr;
  unsigned long lane = strtoul(laneText.c_str(), &laneEnd, 10);
  unsigned long duration = strtoul(durationText.c_str(), &durationEnd, 10);
  if (laneEnd == laneText.c_str() || *laneEnd != '\0' || lane >= LaneCount ||
      durationEnd == durationText.c_str() || *durationEnd != '\0' ||
      duration == 0 || duration > MaxDiagnosticRelayPulseMillis) {
    sendFrame(F("ERR:BAD_DIAG_RELAY"));
    return;
  }
  lastDiagnosticCommandMillis = millis();
  diagnosticRelayPulseActive = true;
  diagnosticRelayPulseLane = (byte)lane;
  diagnosticRelayRestoreMask = trackPowerEnabledMask;
  diagnosticRelayPulseEndsMillis = millis() + duration;
  setTrackPowerMask(trackPowerEnabledMask & (byte)~(1 << lane));
  sendFrame(String(F("DIAG:RELAY:")) + lane + F(":PULSING:") +
            toHexByte(trackPowerEnabledMask) + F(":") + millis());
}

void handleYatssCommand(String command) {
  command.trim();
  if (!stripAndValidateChecksum(command)) {
    sendFrame(F("ERR:BAD_CHECKSUM"));
    return;
  }
  lastWindowsCommandMillis = millis();
  windowsWatchdogArmed = true;

  if (command == "RESET") {
    setTrackPowerMask(0);
    diagnosticMode = false;
    diagnosticRelayPulseActive = false;
    windowsWatchdogArmed = false;
    flushSensorQueue();
    for (byte lane = 0; lane < LaneCount; lane++) {
      laneSequences[lane] = 0;
      lastEdgeMillis[lane] = 0;
    }
    sendFrame(F("HELLO:RESETTING"));
    sendControllerHello();
  } else if (command == "TRACK_POWER:OFF") {
    setTrackPowerMask(0);
    sendFrame(F("HELLO:TRACK_POWER:OFF"));
  } else if (command == "TRACK_POWER:ON") {
    setTrackPowerMask(0xFF);
    sendFrame(F("HELLO:TRACK_POWER:ON"));
  } else if (command.startsWith("TRACK_POWER:MASK:")) {
    String text = command.substring(17);
    char *end = nullptr;
    unsigned long mask = strtoul(text.c_str(), &end, 16);
    if (text.length() != 2 || end == text.c_str() || *end != '\0' || mask > 255) {
      sendFrame(F("ERR:BAD_POWER_MASK"));
      return;
    }
    setTrackPowerMask((byte)mask);
    sendFrame(String(F("HELLO:TRACK_POWER:MASK:")) + text);
  } else if (command.startsWith("CONFIG:DEBOUNCE:")) {
    String text = command.substring(16);
    char *end = nullptr;
    unsigned long value = strtoul(text.c_str(), &end, 10);
    if (text.length() == 0 || end == text.c_str() || *end != '\0' ||
        value > MaxEdgeDebounceMillis) {
      sendFrame(F("ERR:BAD_DEBOUNCE"));
      return;
    }
    edgeDebounceMillis = value;
    sendFrame(String(F("HELLO:CONFIG:DEBOUNCE:")) + value);
  } else if (command == "DIAG:START") {
    startDiagnosticSession();
  } else if (command == "DIAG:STOP") {
    stopDiagnosticSession("REQUESTED");
  } else if (command == "DIAG:STATUS") {
    if (!diagnosticMode) {
      sendFrame(F("ERR:DIAG_NOT_ACTIVE"));
      return;
    }
    lastDiagnosticCommandMillis = millis();
    sendDiagnosticStatus();
  } else if (command == "DIAG:CLEAR") {
    if (!diagnosticMode) {
      sendFrame(F("ERR:DIAG_NOT_ACTIVE"));
      return;
    }
    lastDiagnosticCommandMillis = millis();
    for (byte lane = 0; lane < LaneCount; lane++) {
      diagnosticTransitionCounts[lane] = 0;
      diagnosticAcceptedEdgeCounts[lane] = 0;
      diagnosticLastAcceptedMillis[lane] = 0;
    }
    sendDiagnosticStatus();
  } else if (command.startsWith("DIAG:RELAY:PULSE:")) {
    handleRelayPulse(command.substring(17));
  } else if (command == "PING") {
    sendControllerHello();
  } else if (command != "KEEPALIVE") {
    sendFrame(String(F("ERR:UNKNOWN_COMMAND:")) + command);
  }
}

void publishQueuedSensorTransitions() {
  while (true) {
    SensorTransition transition;
    noInterrupts();
    if (sensorQueueTail == sensorQueueHead) {
      interrupts();
      return;
    }
    transition.lane = sensorQueue[sensorQueueTail].lane;
    transition.active = sensorQueue[sensorQueueTail].active;
    transition.timestampMillis = sensorQueue[sensorQueueTail].timestampMillis;
    sensorQueueTail = (byte)((sensorQueueTail + 1) & SensorQueueMask);
    interrupts();

    byte lane = transition.lane;
    bool active = transition.active != 0;
    unsigned long now = transition.timestampMillis;
    if (diagnosticMode) {
      diagnosticTransitionCounts[lane]++;
      if (active && (diagnosticLastAcceptedMillis[lane] == 0 ||
                     now - diagnosticLastAcceptedMillis[lane] >= edgeDebounceMillis)) {
        diagnosticAcceptedEdgeCounts[lane]++;
        diagnosticLastAcceptedMillis[lane] = now;
      }
      sendFrame(String(F("DIAG:SENSOR:")) + lane + F(":") +
                (active ? F("ACTIVE") : F("CLEAR")) + F(":") +
                diagnosticTransitionCounts[lane] + F(":") +
                diagnosticAcceptedEdgeCounts[lane] + F(":") + now);
    } else if (active && (lastEdgeMillis[lane] == 0 ||
                          now - lastEdgeMillis[lane] >= edgeDebounceMillis)) {
      lastEdgeMillis[lane] = now;
      laneSequences[lane]++;
      sendFrame(String(F("EDGE:")) + lane + F(":") + laneSequences[lane] + F(":") + now);
    }
  }
}

void publishDroppedSensorTransitions() {
  unsigned long dropped;
  noInterrupts();
  dropped = droppedSensorTransitions;
  droppedSensorTransitions = 0;
  interrupts();
  if (dropped > 0) {
    sendFrame(String(F("ERR:QUEUE_FULL:")) + dropped);
  }
}

void setup() {
  for (byte lane = 0; lane < LaneCount; lane++) {
    digitalWrite(trackPowerCutPins[lane], TrackPowerCutActiveLevel);
    pinMode(trackPowerCutPins[lane], OUTPUT);
    pinMode(sensorPins[lane], INPUT_PULLUP);
  }
  Bridge.begin();
  Bridge.provide_safe("yatss_command", handleYatssCommand);
  for (byte lane = 0; lane < LaneCount; lane++) {
    attachInterrupt(digitalPinToInterrupt(sensorPins[lane]), isrHandlers[lane], CHANGE);
  }
  sendControllerHello();
}

void loop() {
  publishQueuedSensorTransitions();
  publishDroppedSensorTransitions();
  unsigned long now = millis();
  if (now - lastHeartbeatMillis >= HeartbeatIntervalMillis) {
    lastHeartbeatMillis = now;
    sendFrame(String(F("HEARTBEAT:")) + now);
  }
  if (diagnosticRelayPulseActive && (long)(now - diagnosticRelayPulseEndsMillis) >= 0) {
    setTrackPowerMask(diagnosticRelayRestoreMask);
    sendFrame(String(F("DIAG:RELAY:")) + diagnosticRelayPulseLane + F(":RESTORED:") +
              toHexByte(trackPowerEnabledMask) + F(":") + now);
    diagnosticRelayPulseActive = false;
  }
  if (diagnosticMode && now - lastDiagnosticCommandMillis >= DiagnosticSessionTimeoutMillis) {
    stopDiagnosticSession("TIMEOUT");
  }
  if (windowsWatchdogArmed && now - lastWindowsCommandMillis >= WindowsKeepaliveTimeoutMillis) {
    bool powerWasEnabled = trackPowerEnabledMask != 0;
    setTrackPowerMask(0);
    windowsWatchdogArmed = false;
    if (powerWasEnabled) {
      sendFrame(String(F("ERR:WINDOWS_WATCHDOG:")) + now);
    }
  }
}
