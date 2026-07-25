/*
  Slot car lap sensor bridge.

  The Arduino does not count laps. It timestamps sensor edges and sends them
  to the Windows app, which owns debounce, lap validation, counting, logging,
  and reconnect behavior.

  Protocol frames are ASCII lines:
    HELLO:YATSSMC:2:<lane-count>*XX
    HEARTBEAT:<millis>*XX
    EDGE:<zero-based-lane>:<per-lane-sequence>:<millis>*XX
    ERR:QUEUE_FULL:<dropped-count>*XX

  Commands from Windows:
    TRACK_POWER:OFF*XX     cuts power to every lane
    TRACK_POWER:ON*XX      restores power to every lane
    TRACK_POWER:MASK:XX*YY sets the enabled lanes using an 8-bit hex mask
    DIAG:START*XX           starts raw sensor and relay diagnostics
    DIAG:STOP*XX            restores normal lap sensor handling

  XX is a two-digit hex XOR checksum of every character before the '*'.
*/

const byte LaneCount = 8;
const byte QueueSize = 32;
const byte QueueMask = QueueSize - 1;
const unsigned long SerialBaud = 115200;
const unsigned long HeartbeatIntervalMillis = 1000;
const unsigned long WindowsKeepaliveTimeoutMillis = 5000;
const unsigned long DiagnosticSessionTimeoutMillis = 5000;
const unsigned long MaxDiagnosticRelayPulseMillis = 2000;
#define DEFAULT_EDGE_DEBOUNCE_MILLIS 1800UL
#define MAX_EDGE_DEBOUNCE_MILLIS 10000UL
#define TRACK_POWER_CUT_ACTIVE_LEVEL HIGH

static_assert((QueueSize & QueueMask) == 0, "QueueSize must be a power of two");

#if defined(CONFIG_IDF_TARGET_ESP32C6)
const byte sensorPins[LaneCount] = { 0, 1, 2, 3, 6, 7, 10, 11 };
const byte trackPowerCutPins[LaneCount] = { 23, 22, 21, 20, 19, 18, 13, 12 };
#else
const byte sensorPins[LaneCount] = { D2, A4, D4, D5, D6, D7, D8, D9 };
const byte trackPowerCutPins[LaneCount] = { D10, D11, D12, D13, A0, A1, A2, A3 };
#endif

struct EdgeEvent {
  byte lane;
  unsigned long sequence;
  unsigned long timestampMillis;
};

volatile EdgeEvent queue[QueueSize];
volatile byte queueHead = 0;
volatile byte queueTail = 0;
volatile unsigned long laneSequences[LaneCount] = { 0, 0, 0, 0, 0, 0, 0, 0 };
volatile unsigned long lastEdgeMillis[LaneCount] = { 0, 0, 0, 0, 0, 0, 0, 0 };
volatile unsigned long droppedEvents = 0;
volatile unsigned long totalDroppedEvents = 0;
portMUX_TYPE queueMux = portMUX_INITIALIZER_UNLOCKED;
volatile unsigned long edgeDebounceMillis = DEFAULT_EDGE_DEBOUNCE_MILLIS;
unsigned long lastHeartbeatMillis = 0;
unsigned long lastWindowsCommandMillis = 0;
bool windowsWatchdogArmed = false;
volatile bool diagnosticMode = false;
volatile unsigned long diagnosticTransitionCounts[LaneCount] = { 0, 0, 0, 0, 0, 0, 0, 0 };
volatile unsigned long diagnosticAcceptedEdgeCounts[LaneCount] = { 0, 0, 0, 0, 0, 0, 0, 0 };
volatile unsigned long diagnosticLastAcceptedMillis[LaneCount] = { 0, 0, 0, 0, 0, 0, 0, 0 };
volatile unsigned long diagnosticLastTransitionMillis[LaneCount] = { 0, 0, 0, 0, 0, 0, 0, 0 };
volatile byte diagnosticChangedMask = 0;
volatile byte diagnosticSensorActiveMask = 0;
unsigned long lastDiagnosticCommandMillis = 0;
byte trackPowerEnabledMask = 0;
bool diagnosticRelayPulseActive = false;
byte diagnosticRelayPulseLane = 0;
byte diagnosticRelayRestoreMask = 0;
unsigned long diagnosticRelayPulseEndsMillis = 0;

void IRAM_ATTR enqueueEdge(byte lane) {
  unsigned long now = millis();

  portENTER_CRITICAL_ISR(&queueMux);
  if (diagnosticMode) {
    byte laneBit = (byte)(1 << lane);
    bool active = digitalRead(sensorPins[lane]) == LOW;
    if (active) {
      diagnosticSensorActiveMask |= laneBit;
    } else {
      diagnosticSensorActiveMask &= (byte)~laneBit;
    }
    diagnosticTransitionCounts[lane]++;
    if (active &&
        (diagnosticLastAcceptedMillis[lane] == 0 ||
         now - diagnosticLastAcceptedMillis[lane] >= edgeDebounceMillis)) {
      diagnosticAcceptedEdgeCounts[lane]++;
      diagnosticLastAcceptedMillis[lane] = now;
    }
    diagnosticLastTransitionMillis[lane] = now;
    diagnosticChangedMask |= laneBit;
    portEXIT_CRITICAL_ISR(&queueMux);
    return;
  }

  if (lastEdgeMillis[lane] != 0 && now - lastEdgeMillis[lane] < edgeDebounceMillis) {
    portEXIT_CRITICAL_ISR(&queueMux);
    return;
  }

  byte nextHead = (byte)((queueHead + 1) & QueueMask);
  if (nextHead == queueTail) {
    droppedEvents++;
    totalDroppedEvents++;
    portEXIT_CRITICAL_ISR(&queueMux);
    return;
  }

  queue[queueHead].lane = lane;
  queue[queueHead].sequence = ++laneSequences[lane];
  queue[queueHead].timestampMillis = now;
  lastEdgeMillis[lane] = now;
  queueHead = nextHead;
  portEXIT_CRITICAL_ISR(&queueMux);
}

void IRAM_ATTR isrLane0() { enqueueEdge(0); }
void IRAM_ATTR isrLane1() { enqueueEdge(1); }
void IRAM_ATTR isrLane2() { enqueueEdge(2); }
void IRAM_ATTR isrLane3() { enqueueEdge(3); }
void IRAM_ATTR isrLane4() { enqueueEdge(4); }
void IRAM_ATTR isrLane5() { enqueueEdge(5); }
void IRAM_ATTR isrLane6() { enqueueEdge(6); }
void IRAM_ATTR isrLane7() { enqueueEdge(7); }

void (*isrHandlers[LaneCount])() = {
  isrLane0, isrLane1, isrLane2, isrLane3,
  isrLane4, isrLane5, isrLane6, isrLane7
};

void setup() {
  for (byte lane = 0; lane < LaneCount; lane++) {
    digitalWrite(trackPowerCutPins[lane], TRACK_POWER_CUT_ACTIVE_LEVEL);
    pinMode(trackPowerCutPins[lane], OUTPUT);
  }

  Serial.begin(SerialBaud);
  Serial.setTimeout(10);
  delay(1000);

  for (byte lane = 0; lane < LaneCount; lane++) {
    pinMode(sensorPins[lane], INPUT_PULLUP);
    attachInterrupt(digitalPinToInterrupt(sensorPins[lane]), isrHandlers[lane], FALLING);
  }

  sendFrame(String(F("HELLO:YATSSMC:2:")) + LaneCount);
}

void loop() {
  publishQueuedEdges();
  publishDroppedEvents();
  publishHeartbeat();
  handleCommands();
  publishDiagnosticSensorChanges();
  serviceDiagnosticRelayPulse();
  serviceDiagnosticSessionTimeout();
  serviceWindowsWatchdog();
}

void publishQueuedEdges() {
  while (true) {
    EdgeEvent event;

    portENTER_CRITICAL(&queueMux);
    if (queueTail == queueHead) {
      portEXIT_CRITICAL(&queueMux);
      return;
    }

    event.lane = queue[queueTail].lane;
    event.sequence = queue[queueTail].sequence;
    event.timestampMillis = queue[queueTail].timestampMillis;
    queueTail = (byte)((queueTail + 1) & QueueMask);
    portEXIT_CRITICAL(&queueMux);

    sendFrame(String(F("EDGE:")) + event.lane + F(":") + event.sequence + F(":") + event.timestampMillis);
  }
}

void publishDroppedEvents() {
  unsigned long dropped;

  portENTER_CRITICAL(&queueMux);
  dropped = droppedEvents;
  droppedEvents = 0;
  portEXIT_CRITICAL(&queueMux);

  if (dropped > 0) {
    sendFrame(String(F("ERR:QUEUE_FULL:")) + dropped);
  }
}

void publishHeartbeat() {
  unsigned long now = millis();
  if (now - lastHeartbeatMillis < HeartbeatIntervalMillis) {
    return;
  }

  lastHeartbeatMillis = now;
  sendFrame(String(F("HEARTBEAT:")) + now);
}

void handleCommands() {
  if (Serial.available() <= 0) {
    return;
  }

  String command = Serial.readStringUntil('\n');
  command.trim();

  if (!stripAndValidateChecksum(command)) {
    sendFrame(String(F("ERR:BAD_CHECKSUM")));
    return;
  }

  lastWindowsCommandMillis = millis();
  windowsWatchdogArmed = true;

  if (command == "RESET") {
    sendFrame(String(F("HELLO:RESETTING")));
    Serial.flush();
    delay(100);
    ESP.restart();
  } else if (command == "TRACK_POWER:OFF") {
    applyTrackPowerCommand(0);
    sendFrame(String(F("HELLO:TRACK_POWER:OFF")));
  } else if (command == "TRACK_POWER:ON") {
    applyTrackPowerCommand(0xFF);
    sendFrame(String(F("HELLO:TRACK_POWER:ON")));
  } else if (command.startsWith("TRACK_POWER:MASK:")) {
    String maskText = command.substring(17);
    char *end = NULL;
    unsigned long mask = strtoul(maskText.c_str(), &end, 16);
    if (maskText.length() != 2 || end == maskText.c_str() || *end != '\0' || mask > 0xFF) {
      sendFrame(String(F("ERR:BAD_POWER_MASK")));
      return;
    }

    applyTrackPowerCommand((byte)mask);
    sendFrame(String(F("HELLO:TRACK_POWER:MASK:")) + maskText);
  } else if (command.startsWith("CONFIG:DEBOUNCE:")) {
    String debounceText = command.substring(16);
    char *end = NULL;
    unsigned long debounce = strtoul(debounceText.c_str(), &end, 10);
    if (debounceText.length() == 0 || end == debounceText.c_str() || *end != '\0' || debounce > MAX_EDGE_DEBOUNCE_MILLIS) {
      sendFrame(String(F("ERR:BAD_DEBOUNCE")));
      return;
    }

    portENTER_CRITICAL(&queueMux);
    edgeDebounceMillis = debounce;
    portEXIT_CRITICAL(&queueMux);
    sendFrame(String(F("HELLO:CONFIG:DEBOUNCE:")) + debounce);
  } else if (command == "DIAG:START") {
    startDiagnosticSession();
  } else if (command == "DIAG:STOP") {
    stopDiagnosticSession("REQUESTED");
  } else if (command == "DIAG:STATUS") {
    if (!diagnosticMode) {
      sendFrame(String(F("ERR:DIAG_NOT_ACTIVE")));
      return;
    }

    lastDiagnosticCommandMillis = millis();
    sendDiagnosticStatus();
  } else if (command == "DIAG:CLEAR") {
    if (!diagnosticMode) {
      sendFrame(String(F("ERR:DIAG_NOT_ACTIVE")));
      return;
    }

    lastDiagnosticCommandMillis = millis();
    portENTER_CRITICAL(&queueMux);
    for (byte lane = 0; lane < LaneCount; lane++) {
      diagnosticTransitionCounts[lane] = 0;
      diagnosticAcceptedEdgeCounts[lane] = 0;
      diagnosticLastAcceptedMillis[lane] = 0;
    }
    portEXIT_CRITICAL(&queueMux);
    sendDiagnosticStatus();
  } else if (command.startsWith("DIAG:RELAY:PULSE:")) {
    handleDiagnosticRelayPulse(command.substring(17));
  } else if (command == "PING") {
    sendFrame(String(F("HELLO:YATSSMC:2:8")));
  } else if (command == "KEEPALIVE") {
    return;
  } else if (command.length() > 0) {
    sendFrame(String(F("ERR:UNKNOWN_COMMAND:")) + command);
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

void startDiagnosticSession() {
  byte sensorMask = readActiveSensorMask();
  portENTER_CRITICAL(&queueMux);
  diagnosticMode = true;
  diagnosticChangedMask = 0;
  diagnosticSensorActiveMask = sensorMask;
  for (byte lane = 0; lane < LaneCount; lane++) {
    diagnosticTransitionCounts[lane] = 0;
    diagnosticAcceptedEdgeCounts[lane] = 0;
    diagnosticLastAcceptedMillis[lane] = 0;
    diagnosticLastTransitionMillis[lane] = 0;
  }
  portEXIT_CRITICAL(&queueMux);

  for (byte lane = 0; lane < LaneCount; lane++) {
    detachInterrupt(digitalPinToInterrupt(sensorPins[lane]));
    attachInterrupt(digitalPinToInterrupt(sensorPins[lane]), isrHandlers[lane], CHANGE);
  }
  unsigned long now = millis();
  lastDiagnosticCommandMillis = now;
  sendFrame(String(F("DIAG:SESSION:STARTED:")) + now);
  sendDiagnosticStatus();
}

void stopDiagnosticSession(const char *reason) {
  unsigned long now = millis();
  if (diagnosticRelayPulseActive) {
    setTrackPowerMask(diagnosticRelayRestoreMask);
    sendDiagnosticRelayState(diagnosticRelayPulseLane, "RESTORED", now);
    diagnosticRelayPulseActive = false;
  }

  for (byte lane = 0; lane < LaneCount; lane++) {
    detachInterrupt(digitalPinToInterrupt(sensorPins[lane]));
  }

  portENTER_CRITICAL(&queueMux);
  diagnosticMode = false;
  for (byte lane = 0; lane < LaneCount; lane++) {
    lastEdgeMillis[lane] = 0;
  }
  portEXIT_CRITICAL(&queueMux);

  for (byte lane = 0; lane < LaneCount; lane++) {
    attachInterrupt(digitalPinToInterrupt(sensorPins[lane]), isrHandlers[lane], FALLING);
  }

  sendFrame(String(F("DIAG:SESSION:STOPPED:")) + reason + F(":") + now);
}

void sendDiagnosticStatus() {
  unsigned long dropped;
  portENTER_CRITICAL(&queueMux);
  dropped = totalDroppedEvents;
  portEXIT_CRITICAL(&queueMux);

  byte sensorMask = readActiveSensorMask();
  sendFrame(
    String(F("DIAG:STATUS:")) + toHexByte(sensorMask) + F(":") +
    toHexByte(trackPowerEnabledMask) + F(":") + edgeDebounceMillis + F(":") +
    dropped + F(":") + millis());
}

void publishDiagnosticSensorChanges() {
  if (!diagnosticMode) {
    return;
  }

  byte changed;
  portENTER_CRITICAL(&queueMux);
  changed = diagnosticChangedMask;
  diagnosticChangedMask = 0;
  portEXIT_CRITICAL(&queueMux);
  if (changed == 0) {
    return;
  }

  for (byte lane = 0; lane < LaneCount; lane++) {
    byte laneBit = (byte)(1 << lane);
    if ((changed & laneBit) == 0) {
      continue;
    }

    unsigned long transitionCount;
    unsigned long acceptedEdgeCount;
    unsigned long transitionMillis;
    bool active;
    portENTER_CRITICAL(&queueMux);
    transitionCount = diagnosticTransitionCounts[lane];
    acceptedEdgeCount = diagnosticAcceptedEdgeCounts[lane];
    transitionMillis = diagnosticLastTransitionMillis[lane];
    active = (diagnosticSensorActiveMask & laneBit) != 0;
    portEXIT_CRITICAL(&queueMux);
    sendFrame(
      String(F("DIAG:SENSOR:")) + lane + F(":") + (active ? F("ACTIVE") : F("CLEAR")) +
      F(":") + transitionCount + F(":") + acceptedEdgeCount + F(":") + transitionMillis);
  }
}

void handleDiagnosticRelayPulse(String arguments) {
  if (!diagnosticMode) {
    sendFrame(String(F("ERR:DIAG_NOT_ACTIVE")));
    return;
  }
  if (diagnosticRelayPulseActive) {
    sendFrame(String(F("ERR:DIAG_RELAY_BUSY")));
    return;
  }

  int separator = arguments.indexOf(':');
  if (separator <= 0) {
    sendFrame(String(F("ERR:BAD_DIAG_RELAY")));
    return;
  }

  String laneText = arguments.substring(0, separator);
  String durationText = arguments.substring(separator + 1);
  char *laneEnd = NULL;
  char *durationEnd = NULL;
  unsigned long lane = strtoul(laneText.c_str(), &laneEnd, 10);
  unsigned long duration = strtoul(durationText.c_str(), &durationEnd, 10);
  if (laneEnd == laneText.c_str() || *laneEnd != '\0' || lane >= LaneCount ||
      durationEnd == durationText.c_str() || *durationEnd != '\0' ||
      duration == 0 || duration > MaxDiagnosticRelayPulseMillis) {
    sendFrame(String(F("ERR:BAD_DIAG_RELAY")));
    return;
  }

  unsigned long now = millis();
  lastDiagnosticCommandMillis = now;
  diagnosticRelayPulseActive = true;
  diagnosticRelayPulseLane = (byte)lane;
  diagnosticRelayRestoreMask = trackPowerEnabledMask;
  diagnosticRelayPulseEndsMillis = now + duration;
  setTrackPowerMask(trackPowerEnabledMask & (byte)~(1 << lane));
  sendDiagnosticRelayState((byte)lane, "PULSING", now);
}

void serviceDiagnosticRelayPulse() {
  if (!diagnosticRelayPulseActive) {
    return;
  }

  unsigned long now = millis();
  if ((long)(now - diagnosticRelayPulseEndsMillis) < 0) {
    return;
  }

  setTrackPowerMask(diagnosticRelayRestoreMask);
  sendDiagnosticRelayState(diagnosticRelayPulseLane, "RESTORED", now);
  diagnosticRelayPulseActive = false;
}

void sendDiagnosticRelayState(byte lane, const char *state, unsigned long now) {
  sendFrame(
    String(F("DIAG:RELAY:")) + lane + F(":") + state + F(":") +
    toHexByte(trackPowerEnabledMask) + F(":") + now);
}

void applyTrackPowerCommand(byte enabledLaneMask) {
  bool canceledPulse = diagnosticRelayPulseActive;
  byte pulseLane = diagnosticRelayPulseLane;
  diagnosticRelayPulseActive = false;
  setTrackPowerMask(enabledLaneMask);
  if (canceledPulse && diagnosticMode) {
    sendDiagnosticRelayState(pulseLane, "RESTORED", millis());
  }
}

void serviceDiagnosticSessionTimeout() {
  if (!diagnosticMode) {
    return;
  }

  unsigned long now = millis();
  if (now - lastDiagnosticCommandMillis >= DiagnosticSessionTimeoutMillis) {
    stopDiagnosticSession("TIMEOUT");
  }
}

void serviceWindowsWatchdog() {
  if (!windowsWatchdogArmed) {
    return;
  }

  unsigned long now = millis();
  if (now - lastWindowsCommandMillis < WindowsKeepaliveTimeoutMillis) {
    return;
  }

  bool powerWasEnabled = trackPowerEnabledMask != 0;
  diagnosticRelayPulseActive = false;
  setTrackPowerMask(0);
  windowsWatchdogArmed = false;
  if (powerWasEnabled) {
    sendFrame(String(F("ERR:WINDOWS_WATCHDOG:")) + now);
  }
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

void sendFrame(String body) {
  byte checksum = calculateChecksum(body);
  Serial.print(body);
  Serial.print(F("*"));
  if (checksum < 16) {
    Serial.print(F("0"));
  }
  Serial.println(checksum, HEX);
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

  char *end = NULL;
  unsigned long expected = strtoul(checksumText.c_str(), &end, 16);
  if (end == checksumText.c_str() || *end != '\0' || expected > 255) {
    return false;
  }

  byte actual = calculateChecksum(body);
  if (actual != (byte)expected) {
    return false;
  }

  line = body;
  return true;
}

void setTrackPowerMask(byte enabledLaneMask) {
  trackPowerEnabledMask = enabledLaneMask;
  byte cutLevel = TRACK_POWER_CUT_ACTIVE_LEVEL;
  byte restoreLevel = (TRACK_POWER_CUT_ACTIVE_LEVEL == HIGH) ? LOW : HIGH;
  for (byte lane = 0; lane < LaneCount; lane++) {
    bool enabled = (enabledLaneMask & (1 << lane)) != 0;
    digitalWrite(trackPowerCutPins[lane], enabled ? restoreLevel : cutLevel);
  }
}

byte calculateChecksum(const String &body) {
  byte checksum = 0;
  for (unsigned int i = 0; i < body.length(); i++) {
    checksum ^= (byte)body[i];
  }

  return checksum;
}
