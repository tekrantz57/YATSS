// ESP32 adapter. Edit Controller/YatssController.h for common behavior.
#include <Arduino.h>
#define YATSS_ISR_ATTR IRAM_ATTR
#include "src/YatssController/YatssController.h"

const byte LaneCount = 8;

#if defined(CONFIG_IDF_TARGET_ESP32C6)
const char ControllerBoardProfile[] = "ESP32_C6_DEVKITC1";
const byte sensorPins[LaneCount] = { 0, 1, 2, 3, 6, 7, 10, 11 };
const byte trackPowerCutPins[LaneCount] = { 23, 22, 21, 20, 19, 18, 13, 12 };
#elif defined(CONFIG_IDF_TARGET_ESP32C5)
const char ControllerBoardProfile[] = "ESP32_C5_WAVESHARE_WIFI6_N16R8";
const byte sensorPins[LaneCount] = { 0, 1, 4, 5, 6, 8, 9, 10 };
const byte trackPowerCutPins[LaneCount] = { 2, 3, 7, 13, 14, 23, 24, 25 };
#else
const char ControllerBoardProfile[] = "ARDUINO_NANO_ESP32";
const byte sensorPins[LaneCount] = { D2, A4, D4, D5, D6, D7, D8, D9 };
const byte trackPowerCutPins[LaneCount] = { D10, D11, D12, D13, A0, A1, A2, A3 };
#endif

portMUX_TYPE queueMux = portMUX_INITIALIZER_UNLOCKED;

struct Esp32Platform {
  static uint32_t now() { return millis(); }
  static const char* profile() { return ControllerBoardProfile; }
  static uint32_t flashBytes() { return ESP.getFlashChipSize(); }
  static void lock() { portENTER_CRITICAL(&queueMux); }
  static void unlock() { portEXIT_CRITICAL(&queueMux); }
  static void sendFrame(const char* frame) { Serial.println(frame); }
  static void reset() { Serial.flush(); delay(100); ESP.restart(); }
  static void setPower(uint8_t mask) {
    for (byte lane = 0; lane < LaneCount; ++lane) {
      digitalWrite(trackPowerCutPins[lane], yatss::trackPowerOutputHigh(mask, lane) ? HIGH : LOW);
    }
  }
  static uint8_t sensorMask() {
    uint8_t mask = 0;
    for (byte lane = 0; lane < LaneCount; ++lane) {
      if (digitalRead(sensorPins[lane]) == LOW) mask |= uint8_t(1u << lane);
    }
    return mask;
  }
  static void configureCapture(bool diagnostics);
};

yatss::Controller<Esp32Platform, 32> controller;

void IRAM_ATTR enqueueEdge(byte lane) {
  uint32_t now = millis();
  portENTER_CRITICAL_ISR(&queueMux);
  controller.capture(lane, !controller.diagnosticsActive() || digitalRead(sensorPins[lane]) == LOW, now);
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

void Esp32Platform::configureCapture(bool diagnostics) {
  for (byte lane = 0; lane < LaneCount; ++lane) {
    detachInterrupt(digitalPinToInterrupt(sensorPins[lane]));
    attachInterrupt(digitalPinToInterrupt(sensorPins[lane]), isrHandlers[lane], diagnostics ? CHANGE : FALLING);
  }
}

void setup() {
  for (byte lane = 0; lane < LaneCount; ++lane) {
    digitalWrite(trackPowerCutPins[lane], LOW);
    pinMode(trackPowerCutPins[lane], OUTPUT);
  }
  Serial.begin(115200);
  Serial.setTimeout(10);
  delay(1000);
  for (byte lane = 0; lane < LaneCount; ++lane) pinMode(sensorPins[lane], INPUT_PULLUP);
  Esp32Platform::configureCapture(false);
  controller.begin();
}

void loop() {
  controller.service();
  if (Serial.available() > 0) {
    String command = Serial.readStringUntil('\n');
    command.trim();
    controller.command(command.c_str());
  }
}
