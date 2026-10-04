// UNO Q adapter. Edit Controller/YatssController.h for common behavior.
#include <Arduino_RouterBridge.h>
#include <Arduino_LED_Matrix.h>
#include <zephyr/irq.h>
#include "src/YatssController/YatssController.h"

const byte LaneCount = 8;
const byte TrackPowerCutActiveLevel = HIGH;
const byte sensorPins[LaneCount] = { D2, D3, D4, D5, D6, D7, D8, D9 };
const byte trackPowerCutPins[LaneCount] = { D10, D11, D12, D13, A0, A1, A2, A3 };
Arduino_LED_Matrix statusMatrix;

struct UnoQPlatform {
  static unsigned int interruptKey;
  static uint32_t now() { return millis(); }
  static const char* profile() { return "ARDUINO_UNO_Q_STM32U585"; }
  static uint32_t flashBytes() { return 2097152; }
  static void lock() { interruptKey = irq_lock(); }
  static void unlock() { irq_unlock(interruptKey); }
  static void sendFrame(const char* frame) { Bridge.notify("yatss_frame", String(frame)); }
  static void reset() { } // RouterBridge stays alive; the common core resets session state.
  static void configureCapture(bool diagnostics) { (void)diagnostics; }
  static void setPower(uint8_t mask) {
    for (byte lane = 0; lane < LaneCount; ++lane) {
      byte restoreLevel = TrackPowerCutActiveLevel == HIGH ? LOW : HIGH;
      digitalWrite(trackPowerCutPins[lane], (mask & (1u << lane)) ? restoreLevel : TrackPowerCutActiveLevel);
    }
  }
  static uint8_t sensorMask() {
    uint8_t mask = 0;
    for (byte lane = 0; lane < LaneCount; ++lane) {
      if (digitalRead(sensorPins[lane]) == LOW) mask |= uint8_t(1u << lane);
    }
    return mask;
  }
};
unsigned int UnoQPlatform::interruptKey = 0;
yatss::Controller<UnoQPlatform, 64> controller;

enum class MatrixStatus : byte { Waiting, Healthy, Problem };
MatrixStatus displayedMatrixStatus = (MatrixStatus)255;
const uint8_t WaitingFrame[8][13] = {
  { 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0 },
  { 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0 },
  { 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0 },
  { 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0 },
  { 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 },
  { 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 },
  { 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 },
  { 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 }
};
const uint8_t HealthyFrame[8][13] = {
  { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
  { 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1 },
  { 1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 1 },
  { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 },
  { 1, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 1 },
  { 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1 },
  { 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1 },
  { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }
};
const uint8_t ProblemFrame[8][13] = {
  { 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0 },
  { 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0 },
  { 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0 },
  { 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0 },
  { 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0 },
  { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
  { 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0 },
  { 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0 }
};

void refreshMatrixStatus() {
  MatrixStatus status = controller.problem() ? MatrixStatus::Problem
    : (controller.watchdogArmed() ? MatrixStatus::Healthy : MatrixStatus::Waiting);
  if (status == displayedMatrixStatus) return;
  displayedMatrixStatus = status;
  if (status == MatrixStatus::Problem) statusMatrix.draw(&ProblemFrame[0][0]);
  else if (status == MatrixStatus::Healthy) statusMatrix.draw(&HealthyFrame[0][0]);
  else statusMatrix.draw(&WaitingFrame[0][0]);
}

void enqueueSensorTransition(byte lane) {
  uint32_t now = millis();
  unsigned int key = irq_lock();
  controller.capture(lane, digitalRead(sensorPins[lane]) == LOW, now);
  irq_unlock(key);
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

void handleYatssCommand(String command) {
  command.trim();
  controller.command(command.c_str());
  refreshMatrixStatus();
}

void setup() {
  for (byte lane = 0; lane < LaneCount; ++lane) {
    digitalWrite(trackPowerCutPins[lane], TrackPowerCutActiveLevel);
    pinMode(trackPowerCutPins[lane], OUTPUT);
    pinMode(sensorPins[lane], INPUT_PULLUP);
  }
  statusMatrix.begin();
  statusMatrix.setGrayscaleBits(1);
  refreshMatrixStatus();
  Bridge.begin();
  Bridge.provide_safe("yatss_command", handleYatssCommand);
  for (byte lane = 0; lane < LaneCount; ++lane) {
    attachInterrupt(digitalPinToInterrupt(sensorPins[lane]), isrHandlers[lane], CHANGE);
  }
  controller.begin();
}

void loop() {
  controller.service();
  refreshMatrixStatus();
}
