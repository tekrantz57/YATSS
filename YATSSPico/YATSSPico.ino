// Pico 2 W adapter. Edit Controller/YatssController.h for common behavior.
#include <Arduino.h>
#include <hardware/watchdog.h>
#include <pico/critical_section.h>
#include <pico/rand.h>
#include "src/YatssController/YatssController.h"

#if !defined(ARDUINO_RASPBERRY_PI_PICO_2W) || !defined(PICO_RP2350)
#error "Select Raspberry Pi Pico 2W in the Earle Philhower Arduino-Pico core."
#endif

const byte LaneCount = 8;
const byte sensorPins[LaneCount] = { 0, 1, 2, 3, 4, 5, 6, 7 };
const byte trackPowerCutPins[LaneCount] = { 8, 9, 10, 11, 12, 13, 14, 15 };
critical_section_t controllerLock;

struct PicoPlatform {
  static bool randomSession(uint32_t& high, uint32_t& low) {
    // SDK entropy-seeded generator; session IDs are not authentication secrets.
    const uint64_t value = get_rand_64();
    high = uint32_t(value >> 32); low = uint32_t(value);
    return true;
  }
  static uint32_t now() { return millis(); }
  static const char* profile() { return "RASPBERRY_PI_PICO_2_W"; }
  static uint32_t flashBytes() { return PICO_FLASH_SIZE_BYTES; }
  static void lock() { critical_section_enter_blocking(&controllerLock); }
  static void unlock() { critical_section_exit(&controllerLock); }
  static void sendFrame(const char* frame) { Serial.println(frame); }
  static void reset() { Serial.flush(); delay(100); watchdog_reboot(0, 0, 0); while (true) {} }
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
  static void configureCapture(bool diagnostics, uint8_t mask);
};

yatss::Controller<PicoPlatform, 32> controller;

void enqueueEdge(byte lane) {
  const uint32_t now = millis();
  PicoPlatform::lock();
  controller.capture(lane, !controller.diagnosticsActive() || digitalRead(sensorPins[lane]) == LOW, now);
  PicoPlatform::unlock();
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

void PicoPlatform::configureCapture(bool diagnostics, uint8_t mask) {
  for (byte lane = 0; lane < LaneCount; ++lane) {
    detachInterrupt(digitalPinToInterrupt(sensorPins[lane]));
    if (mask & (1u << lane))
      attachInterrupt(digitalPinToInterrupt(sensorPins[lane]), isrHandlers[lane], diagnostics ? CHANGE : FALLING);
  }
}

void setup() {
  for (byte lane = 0; lane < LaneCount; ++lane) {
    digitalWrite(trackPowerCutPins[lane], LOW);
    pinMode(trackPowerCutPins[lane], OUTPUT);
  }
  critical_section_init(&controllerLock);
  Serial.begin(115200);
  Serial.setTimeout(10);
  // No wait-for-host loop: controller safety must run without an open COM port.
  for (byte lane = 0; lane < LaneCount; ++lane) pinMode(sensorPins[lane], INPUT_PULLUP);
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
