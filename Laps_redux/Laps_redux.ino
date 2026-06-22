/*
  Slot car lap sensor bridge.

  The Arduino does not count laps. It timestamps sensor edges and sends them
  to the Windows app, which owns debounce, lap validation, counting, logging,
  and reconnect behavior.

  Protocol frames are ASCII lines:
    HELLO:LAPS_REDUX:2:<lane-count>*XX
    EDGE:<zero-based-lane>:<per-lane-sequence>:<millis>*XX
    ERR:QUEUE_FULL:<dropped-count>*XX

  XX is a two-digit hex XOR checksum of every character before the '*'.
*/

const byte LaneCount = 8;
const byte QueueSize = 32;
const unsigned long SerialBaud = 115200;

const byte sensorPins[LaneCount] = { 12, 11, 10, 9, 8, 7, 6, 5 };

struct EdgeEvent {
  byte lane;
  unsigned long sequence;
  unsigned long timestampMillis;
};

volatile EdgeEvent queue[QueueSize];
volatile byte queueHead = 0;
volatile byte queueTail = 0;
volatile unsigned long laneSequences[LaneCount] = { 0, 0, 0, 0, 0, 0, 0, 0 };
volatile unsigned long droppedEvents = 0;

void enqueueEdge(byte lane) {
  byte nextHead = (byte)((queueHead + 1) % QueueSize);
  if (nextHead == queueTail) {
    droppedEvents++;
    return;
  }

  queue[queueHead].lane = lane;
  queue[queueHead].sequence = ++laneSequences[lane];
  queue[queueHead].timestampMillis = millis();
  queueHead = nextHead;
}

void isrLane0() { enqueueEdge(0); }
void isrLane1() { enqueueEdge(1); }
void isrLane2() { enqueueEdge(2); }
void isrLane3() { enqueueEdge(3); }
void isrLane4() { enqueueEdge(4); }
void isrLane5() { enqueueEdge(5); }
void isrLane6() { enqueueEdge(6); }
void isrLane7() { enqueueEdge(7); }

void (*isrHandlers[LaneCount])() = {
  isrLane0, isrLane1, isrLane2, isrLane3,
  isrLane4, isrLane5, isrLane6, isrLane7
};

void (*resetFunc)(void) = 0;

void setup() {
  Serial.begin(SerialBaud);
  delay(1000);

  for (byte lane = 0; lane < LaneCount; lane++) {
    pinMode(sensorPins[lane], INPUT_PULLUP);
    attachInterrupt(digitalPinToInterrupt(sensorPins[lane]), isrHandlers[lane], FALLING);
  }

  sendFrame(String(F("HELLO:LAPS_REDUX:2:")) + LaneCount);
}

void loop() {
  publishQueuedEdges();
  publishDroppedEvents();
  handleCommands();
}

void publishQueuedEdges() {
  while (true) {
    EdgeEvent event;

    noInterrupts();
    if (queueTail == queueHead) {
      interrupts();
      return;
    }

    event.lane = queue[queueTail].lane;
    event.sequence = queue[queueTail].sequence;
    event.timestampMillis = queue[queueTail].timestampMillis;
    queueTail = (byte)((queueTail + 1) % QueueSize);
    interrupts();

    sendFrame(String(F("EDGE:")) + event.lane + F(":") + event.sequence + F(":") + event.timestampMillis);
  }
}

void publishDroppedEvents() {
  unsigned long dropped;

  noInterrupts();
  dropped = droppedEvents;
  droppedEvents = 0;
  interrupts();

  if (dropped > 0) {
    sendFrame(String(F("ERR:QUEUE_FULL:")) + dropped);
  }
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
    delay(100);
    resetFunc();
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

byte calculateChecksum(const String &body) {
  byte checksum = 0;
  for (unsigned int i = 0; i < body.length(); i++) {
    checksum ^= (byte)body[i];
  }

  return checksum;
}
