#include <Arduino.h>
#include <stdlib.h>
#include <string.h>
#include "C3RmtOutput.h"

#if !defined(CONFIG_IDF_TARGET_ESP32C3)
#error Select an ESP32-C3 board.
#endif

// Confirm GPIO4 is exposed and unused on the actual C3 Mini before wiring.
constexpr uint8_t OUTPUT_PIN = 4;
const uint8_t pins[4] = { OUTPUT_PIN, OUTPUT_PIN, OUTPUT_PIN, OUTPUT_PIN };
bool npnMode = false;
struct Output {
  uint32_t now() { return micros(); }
  void set(uint8_t channel, bool blocked) {
    if (channel == 0) digitalWrite(OUTPUT_PIN, (blocked == npnMode) ? HIGH : LOW);
  }
} output;
C3RmtOutput<Output> rmt(output, pins);
DragPulse::RmtTrain<C3RmtOutput<Output>> train(rmt);
DragPulse::Config config = {};
char line[96];
size_t used = 0;
bool overflow = false, heldLow = false, rmtReady = false;

bool number(const char* s, uint32_t& value) {
  if (!s || !*s) return false;
  value = 0;
  while (*s) {
    if (*s < '0' || *s > '9') return false;
    uint8_t d = *s++ - '0';
    if (value > (UINT32_MAX - d) / 10) return false;
    value = value * 10 + d;
  }
  return true;
}

void printRange(const DragPulse::Range& range) {
  if (range.minimum == UINT32_MAX) Serial.print("n/a");
  else { Serial.print(range.minimum); Serial.print('-'); Serial.print(range.maximum); }
}

void report(const char* result) {
  Serial.print(result); Serial.print(" requested="); Serial.print(config.count);
  Serial.print(" completed="); Serial.print(train.completed);
  Serial.print(" programmed_width_us="); Serial.print(config.widthUs);
  Serial.print(" requested_gap_us="); printRange(train.requestedGaps);
  Serial.print(" observed_rearm_wait_us="); printRange(train.rearmWait);
  Serial.print(" max_scheduler_late_us="); Serial.println(train.maxLateUs);
  Serial.println("Widths are programmed, not measured; gaps may stretch. Interrupted pulses may be unconfirmed.");
}

void command() {
  char* args[6]; size_t n = 0;
  char* part = strtok(line, " \t\r");
  while (part && n < 6) { args[n++] = part; part = strtok(nullptr, " \t\r"); }
  if (!n) return;
  if (n == 1 && !strcmp(args[0], "STOP")) {
    bool active = train.running;
    if (active) {
      train.stop();
      if (!rmt.setLevel(!npnMode)) Serial.println("RMT clear level failed; disconnect signal");
      report("STOPPED");
    }
    else { if (!rmt.setLevel(!npnMode)) { Serial.println("RMT level failed"); return; } heldLow = false; Serial.println("IDLE HIGH"); }
    return;
  }
  if (train.running) { Serial.println("Busy: STOP only"); return; }
  if (n == 1 && !strcmp(args[0], "HELP")) {
    Serial.println("YATSS pulse fixture: GPIO4, active LOW; USB serial 115200 newline");
    Serial.println("RUN width_us gap_us count jitter_us | STOP | LEVEL 0|1 | MODE DIRECT|NPN | HELP");
    Serial.println("width 100..1000000, gap 100..60000000, count 1..1000000, jitter 0..1000000 us");
    Serial.println("LEVEL 0=receiver LOW, 1=receiver HIGH; return clear before RUN.");
    Serial.println("MODE changes output idle level: disconnect signal first. Read wiring guide.");
  } else if (n == 2 && !strcmp(args[0], "LEVEL") && (!strcmp(args[1], "0") || !strcmp(args[1], "1"))) {
    bool requestedLow = args[1][0] == '0';
    if (!rmt.setLevel(requestedLow == npnMode)) { Serial.println("RMT level failed"); return; }
    heldLow = requestedLow;
    Serial.println(heldLow ? "LEVEL LOW" : "LEVEL HIGH");
  } else if (n == 5 && !strcmp(args[0], "RUN")) {
    DragPulse::Config next = {}; next.mask = 1;
    if (heldLow) { Serial.println("Set LEVEL 1 before RUN"); return; }
    if (!number(args[1], next.widthUs) || !number(args[2], next.gapUs) ||
        !number(args[3], next.count) || !number(args[4], next.jitterUs) || !DragPulse::valid(next)) {
      Serial.println("Invalid RUN; see HELP"); return;
    }
    config = next;
    if (!rmtReady) { Serial.println("RMT not ready; disconnect signal and restart fixture"); return; }
    if (!train.begin(config, !npnMode)) { Serial.println("RMT setup failed"); return; }
    Serial.print("RUN requested="); Serial.println(config.count);
  } else if (n == 2 && !strcmp(args[0], "MODE") && (!strcmp(args[1], "DIRECT") || !strcmp(args[1], "NPN"))) {
    if (heldLow) { Serial.println("Set LEVEL 1 before changing mode"); return; }
    if (!rmt.shutdown()) { Serial.println("RMT fault: disconnect signal and restart fixture"); return; }
    npnMode = !strcmp(args[1], "NPN"); output.set(0, false);
    DragPulse::PulsePlan idlePlan;
    idlePlan.build(100, !npnMode);
    rmtReady = rmt.prepare(idlePlan, 0);
    if (!rmtReady) { Serial.println("RMT setup failed; signal must remain disconnected"); return; }
    Serial.println(npnMode ? "MODE NPN: idle GPIO LOW" : "MODE DIRECT: idle GPIO HIGH");
  } else Serial.println("Unknown command; see HELP");
}

void setup() {
  digitalWrite(OUTPUT_PIN, HIGH);
  pinMode(OUTPUT_PIN, OUTPUT);
  Serial.begin(115200);
  DragPulse::PulsePlan idlePlan;
  idlePlan.build(100, true);
  rmtReady = rmt.prepare(idlePlan, 0);
}
void loop() {
  bool wasRunning = train.running;
  train.tick();
  if (wasRunning && !train.running) report(train.failed ? "ERROR" : "DONE");
  while (Serial.available()) {
    char c = char(Serial.read());
    if (c == '\n') {
      line[used] = 0;
      if (overflow) Serial.println("Command too long"); else command();
      used = 0; overflow = false;
    } else if (used < sizeof(line) - 1) line[used++] = c;
    else overflow = true;
  }
}
