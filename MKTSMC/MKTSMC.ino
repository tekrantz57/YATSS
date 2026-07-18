/*
  Slot car lap sensor bridge.

  The Arduino does not count laps. It timestamps sensor edges and sends them
  to the Windows app, which owns debounce, lap validation, counting, logging,
  and reconnect behavior.

  Protocol frames are ASCII lines:
    HELLO:LAPS_REDUX:2:<lane-count>*XX
    HEARTBEAT:<millis>*XX
    EDGE:<zero-based-lane>:<per-lane-sequence>:<millis>*XX
    ERR:QUEUE_FULL:<dropped-count>*XX

  Commands from Windows:
    TRACK_POWER:OFF*XX     cuts power to every lane
    TRACK_POWER:ON*XX      restores power to every lane
    TRACK_POWER:MASK:XX*YY sets the enabled lanes using an 8-bit hex mask

  XX is a two-digit hex XOR checksum of every character before the '*'.
*/

const byte LaneCount = 8;
const byte QueueSize = 32;
const unsigned long SerialBaud = 115200;
const unsigned long HeartbeatIntervalMillis = 1000;
#define DEFAULT_EDGE_DEBOUNCE_MILLIS 1800UL
#define MAX_EDGE_DEBOUNCE_MILLIS 10000UL
#define TRACK_POWER_CUT_ACTIVE_LEVEL HIGH

const byte sensorPins[LaneCount] = { D2, A4, D4, D5, D6, D7, D8, D9 };
const byte trackPowerCutPins[LaneCount] = { D10, D11, D12, D13, A0, A1, A2, A3 };

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
portMUX_TYPE queueMux = portMUX_INITIALIZER_UNLOCKED;
volatile unsigned long edgeDebounceMillis = DEFAULT_EDGE_DEBOUNCE_MILLIS;
unsigned long lastHeartbeatMillis = 0;

void IRAM_ATTR enqueueEdge(byte lane) {
  unsigned long now = millis();

  portENTER_CRITICAL_ISR(&queueMux);
  if (lastEdgeMillis[lane] != 0 && now - lastEdgeMillis[lane] < edgeDebounceMillis) {
    portEXIT_CRITICAL_ISR(&queueMux);
    return;
  }

  byte nextHead = (byte)((queueHead + 1) % QueueSize);
  if (nextHead == queueTail) {
    droppedEvents++;
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
  Serial.begin(SerialBaud);
  delay(1000);

  for (byte lane = 0; lane < LaneCount; lane++) {
    digitalWrite(trackPowerCutPins[lane], TRACK_POWER_CUT_ACTIVE_LEVEL);
    pinMode(trackPowerCutPins[lane], OUTPUT);
  }

  for (byte lane = 0; lane < LaneCount; lane++) {
    pinMode(sensorPins[lane], INPUT_PULLUP);
    attachInterrupt(digitalPinToInterrupt(sensorPins[lane]), isrHandlers[lane], FALLING);
  }

  sendFrame(String(F("HELLO:LAPS_REDUX:2:")) + LaneCount);
}

void loop() {
  publishQueuedEdges();
  publishDroppedEvents();
  publishHeartbeat();
  handleCommands();
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
    queueTail = (byte)((queueTail + 1) % QueueSize);
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

  if (command == "RESET" || command == "R") {
    sendFrame(String(F("HELLO:RESETTING")));
    Serial.flush();
    delay(100);
    ESP.restart();
  } else if (command == "TRACK_POWER:OFF") {
    setTrackPowerMask(0);
    sendFrame(String(F("HELLO:TRACK_POWER:OFF")));
  } else if (command == "TRACK_POWER:ON") {
    setTrackPowerMask(0xFF);
    sendFrame(String(F("HELLO:TRACK_POWER:ON")));
  } else if (command.startsWith("TRACK_POWER:MASK:")) {
    String maskText = command.substring(17);
    char *end = NULL;
    unsigned long mask = strtoul(maskText.c_str(), &end, 16);
    if (maskText.length() != 2 || end == maskText.c_str() || *end != '\0' || mask > 0xFF) {
      sendFrame(String(F("ERR:BAD_POWER_MASK")));
      return;
    }

    setTrackPowerMask((byte)mask);
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
  } else if (command == "PING") {
    sendFrame(String(F("HELLO:LAPS_REDUX:2:8")));
  } else if (command.length() > 0) {
    sendFrame(String(F("ERR:UNKNOWN_COMMAND:")) + command);
  }
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
    return true;
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
