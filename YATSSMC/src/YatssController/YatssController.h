#pragma once

#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <stdarg.h>
#include <string.h>
#include "FirmwareVersion.h"

#ifndef YATSS_ISR_ATTR
#define YATSS_ISR_ATTR
#endif

namespace yatss {

// Platform supplies clock, framed transport, GPIO, critical sections, and reset.
// Sensor capture is called inside the platform's ISR critical section.
template<class Platform, uint8_t QueueSize>
class Controller {
public:
  static constexpr uint8_t LaneCount = 8;
  static constexpr uint32_t HeartbeatInterval = 1000;
  static constexpr uint32_t WatchdogTimeout = 5000;
  static constexpr uint32_t DiagnosticTimeout = 5000;
  static constexpr uint32_t MaxRelayPulse = 2000;
  static constexpr uint32_t MaxDebounce = 10000;
  static_assert(QueueSize >= 2 && (QueueSize & (QueueSize - 1)) == 0,
                "QueueSize must be a power of two between 2 and 128");

  void begin() {
    setPower(0);
    hello();
  }

  void YATSS_ISR_ATTR capture(uint8_t lane, bool active, uint32_t now) {
    if (lane >= LaneCount) return;
    const uint8_t bit = uint8_t(1u << lane);
    if (diagnostic_) {
      if (active) sensorMask_ |= bit;
      else sensorMask_ &= uint8_t(~bit);
      ++transitions_[lane];
      if (active && (!diagnosticBaseline_[lane] || uint32_t(now - diagnosticLast_[lane]) >= debounce_)) {
        ++accepted_[lane];
        diagnosticLast_[lane] = now;
        diagnosticBaseline_[lane] = true;
      }
      transitionTime_[lane] = now;
      changedMask_ |= bit;
      return;
    }
    if (!active || (baseline_[lane] && uint32_t(now - lastEdge_[lane]) < debounce_)) return;
    uint8_t next = uint8_t((head_ + 1) & (QueueSize - 1));
    if (next == tail_) {
      ++dropped_;
      ++totalDropped_;
      return;
    }
    queue_[head_].lane = lane;
    queue_[head_].sequence = ++sequences_[lane];
    queue_[head_].timestamp = now;
    baseline_[lane] = true;
    lastEdge_[lane] = now;
    head_ = next;
  }

  void command(const char* frame) {
    char body[128];
    if (!decode(frame, body, sizeof(body))) {
      send("ERR:BAD_CHECKSUM");
      return;
    }
    uint32_t now = Platform::now();
    lastCommand_ = now;
    watchdogArmed_ = true;
    communicationProblem_ = false;
    if (strcmp(body, "RESET") == 0) {
      pulseActive_ = false;
      setPower(0);
      watchdogArmed_ = false;
      queueProblem_ = false;
      {
        Guard guard;
        diagnostic_ = false;
        head_ = tail_ = 0;
        dropped_ = totalDropped_ = 0;
        changedMask_ = 0;
        for (uint8_t lane = 0; lane < LaneCount; ++lane) {
          sequences_[lane] = lastEdge_[lane] = 0;
          baseline_[lane] = false;
        }
      }
      Platform::configureCapture(false);
      send("HELLO:RESETTING");
      Platform::reset(); // ESP32 reboots; UNO Q retains MCU uptime.
      hello();
    } else if (strcmp(body, "TRACK_POWER:OFF") == 0) {
      applyPower(0);
      send("HELLO:TRACK_POWER:OFF");
    } else if (strcmp(body, "TRACK_POWER:ON") == 0) {
      applyPower(0xFF);
      send("HELLO:TRACK_POWER:ON");
    } else if (startsWith(body, "TRACK_POWER:MASK:")) {
      uint32_t mask;
      if (strlen(body + 17) != 2 || !number(body + 17, 16, 255, mask)) send("ERR:BAD_POWER_MASK");
      else {
        applyPower(uint8_t(mask));
        send("HELLO:TRACK_POWER:MASK:%s", body + 17);
      }
    } else if (startsWith(body, "CONFIG:DEBOUNCE:")) {
      uint32_t value;
      if (!number(body + 16, 10, MaxDebounce, value)) send("ERR:BAD_DEBOUNCE");
      else {
        { Guard guard; debounce_ = value; }
        send("HELLO:CONFIG:DEBOUNCE:%lu", (unsigned long)value);
      }
    } else if (strcmp(body, "DIAG:START") == 0) {
      startDiagnostics(now);
    } else if (strcmp(body, "DIAG:STOP") == 0) {
      stopDiagnostics("REQUESTED", now);
    } else if (strcmp(body, "DIAG:STATUS") == 0 || strcmp(body, "DIAG:CLEAR") == 0) {
      if (!diagnostic_) { send("ERR:DIAG_NOT_ACTIVE"); return; }
      lastDiagnosticCommand_ = now;
      if (strcmp(body, "DIAG:CLEAR") == 0) {
        Guard guard;
        clearDiagnostics();
      }
      diagnosticStatus(now);
    } else if (startsWith(body, "DIAG:RELAY:PULSE:")) {
      relayPulse(body + 17, now);
    } else if (strcmp(body, "PING") == 0) {
      hello();
    } else if (strcmp(body, "KEEPALIVE") != 0 && body[0] != '\0') {
      send("ERR:UNKNOWN_COMMAND:%s", body);
    }
  }

  void service() {
    uint32_t now = Platform::now();
    // Safety deadlines run before transport output or a pending pulse restore.
    if (watchdogArmed_ && uint32_t(now - lastCommand_) >= WatchdogTimeout) {
      bool wasEnabled = powerMask_ != 0;
      pulseActive_ = false;
      setPower(0);
      watchdogArmed_ = false;
      communicationProblem_ = true;
      if (wasEnabled) send("ERR:WINDOWS_WATCHDOG:%lu", (unsigned long)now);
    }
    if (pulseActive_ && uint32_t(now - pulseStarted_) >= pulseDuration_) {
      pulseActive_ = false;
      setPower(pulseRestoreMask_);
      relayState("RESTORED", now);
    }
    if (diagnostic_ && uint32_t(now - lastDiagnosticCommand_) >= DiagnosticTimeout) {
      stopDiagnostics("TIMEOUT", now);
    }
    // Bound each drain so continuously arriving edges cannot starve safety checks.
    for (uint8_t index = 0; index < QueueSize; ++index) {
      Edge edge;
      {
        Guard guard;
        if (tail_ == head_) break;
        edge.lane = queue_[tail_].lane;
        edge.sequence = queue_[tail_].sequence;
        edge.timestamp = queue_[tail_].timestamp;
        tail_ = uint8_t((tail_ + 1) & (QueueSize - 1));
      }
      send("EDGE:%u:%lu:%lu", (unsigned)edge.lane, (unsigned long)edge.sequence, (unsigned long)edge.timestamp);
    }
    uint32_t dropped;
    { Guard guard; dropped = dropped_; dropped_ = 0; }
    if (dropped != 0) {
      queueProblem_ = true;
      send("ERR:QUEUE_FULL:%lu", (unsigned long)dropped);
    }
    publishDiagnostics();
    if (uint32_t(now - lastHeartbeat_) >= HeartbeatInterval) {
      lastHeartbeat_ = now;
      send("HEARTBEAT:%lu", (unsigned long)now);
    }
  }

  bool watchdogArmed() const { return watchdogArmed_; }
  bool YATSS_ISR_ATTR diagnosticsActive() const { return diagnostic_; }
  bool problem() const { return communicationProblem_ || queueProblem_; }
  uint8_t powerMask() const { return powerMask_; }

  static uint8_t checksum(const char* body) {
    uint8_t result = 0;
    while (*body) result ^= uint8_t(*body++);
    return result;
  }

private:
  struct Guard { Guard() { Platform::lock(); } ~Guard() { Platform::unlock(); } };
  struct Edge { uint8_t lane; uint32_t sequence; uint32_t timestamp; };
  volatile Edge queue_[QueueSize] = {};
  volatile uint8_t head_ = 0, tail_ = 0;
  volatile uint32_t sequences_[LaneCount] = {}, lastEdge_[LaneCount] = {};
  volatile bool baseline_[LaneCount] = {};
  volatile uint32_t dropped_ = 0, totalDropped_ = 0;
  volatile uint32_t debounce_ = 1800;
  volatile bool diagnostic_ = false;
  volatile uint32_t transitions_[LaneCount] = {}, accepted_[LaneCount] = {};
  volatile uint32_t diagnosticLast_[LaneCount] = {}, transitionTime_[LaneCount] = {};
  volatile bool diagnosticBaseline_[LaneCount] = {};
  volatile uint8_t sensorMask_ = 0, changedMask_ = 0;
  uint32_t lastHeartbeat_ = 0, lastCommand_ = 0, lastDiagnosticCommand_ = 0;
  bool watchdogArmed_ = false, communicationProblem_ = false, queueProblem_ = false;
  uint8_t powerMask_ = 0;
  bool pulseActive_ = false;
  uint8_t pulseLane_ = 0, pulseRestoreMask_ = 0;
  uint32_t pulseStarted_ = 0, pulseDuration_ = 0;

  static bool startsWith(const char* text, const char* prefix) {
    return strncmp(text, prefix, strlen(prefix)) == 0;
  }
  static int digit(char value) {
    if (value >= '0' && value <= '9') return value - '0';
    if (value >= 'A' && value <= 'F') return value - 'A' + 10;
    if (value >= 'a' && value <= 'f') return value - 'a' + 10;
    return -1;
  }
  static bool number(const char* text, unsigned base, uint32_t maximum, uint32_t& result) {
    if (*text == '\0') return false;
    result = 0;
    for (; *text; ++text) {
      int value = digit(*text);
      if (value < 0 || unsigned(value) >= base || uint32_t(value) > maximum ||
          result > (maximum - uint32_t(value)) / base) return false;
      result = result * base + unsigned(value);
    }
    return true;
  }
  static bool decode(const char* frame, char* body, size_t capacity) {
    const char* marker = strrchr(frame, '*');
    if (!marker || size_t(marker - frame) >= capacity || strlen(marker + 1) != 2) return false;
    uint32_t expected;
    if (!number(marker + 1, 16, 255, expected)) return false;
    size_t length = size_t(marker - frame);
    memcpy(body, frame, length);
    body[length] = '\0';
    return checksum(body) == expected;
  }
  void send(const char* format, ...) {
    char body[192];
    va_list arguments;
    va_start(arguments, format);
    int length = vsnprintf(body, sizeof(body), format, arguments);
    va_end(arguments);
    if (length < 0 || size_t(length) >= sizeof(body)) return;
    char frame[196];
    snprintf(frame, sizeof(frame), "%s*%02X", body, (unsigned)checksum(body));
    Platform::sendFrame(frame);
  }
  void hello() {
    send("HELLO:YATSSMC:4:8:%s:%s:%lu", Platform::profile(), YATSSMC_FIRMWARE_VERSION,
         (unsigned long)Platform::flashBytes());
  }
  void setPower(uint8_t mask) {
    powerMask_ = mask;
    Platform::setPower(mask);
  }
  void applyPower(uint8_t mask) {
    bool cancelled = pulseActive_;
    pulseActive_ = false;
    setPower(mask);
    if (cancelled && diagnostic_) relayState("RESTORED", Platform::now());
  }
  void clearDiagnostics() {
    changedMask_ = 0;
    for (uint8_t lane = 0; lane < LaneCount; ++lane) {
      transitions_[lane] = accepted_[lane] = diagnosticLast_[lane] = transitionTime_[lane] = 0;
      diagnosticBaseline_[lane] = false;
    }
  }
  void startDiagnostics(uint32_t now) {
    {
      Guard guard;
      diagnostic_ = true;
      tail_ = head_;
      clearDiagnostics();
      sensorMask_ = Platform::sensorMask();
    }
    Platform::configureCapture(true);
    lastDiagnosticCommand_ = now;
    send("DIAG:SESSION:STARTED:%lu", (unsigned long)now);
    diagnosticStatus(now);
  }
  void stopDiagnostics(const char* reason, uint32_t now) {
    if (pulseActive_) {
      pulseActive_ = false;
      setPower(pulseRestoreMask_);
      relayState("RESTORED", now);
    }
    {
      Guard guard;
      diagnostic_ = false;
      tail_ = head_;
      changedMask_ = 0;
      for (uint8_t lane = 0; lane < LaneCount; ++lane) baseline_[lane] = false;
    }
    Platform::configureCapture(false);
    send("DIAG:SESSION:STOPPED:%s:%lu", reason, (unsigned long)now);
  }
  void diagnosticStatus(uint32_t now) {
    uint32_t dropped;
    { Guard guard; dropped = totalDropped_; }
    send("DIAG:STATUS:%02X:%02X:%lu:%lu:%lu", (unsigned)Platform::sensorMask(),
         (unsigned)powerMask_, (unsigned long)debounce_, (unsigned long)dropped, (unsigned long)now);
  }
  void publishDiagnostics() {
    uint8_t changed;
    { Guard guard; changed = changedMask_; changedMask_ = 0; }
    for (uint8_t lane = 0; lane < LaneCount; ++lane) {
      if (!(changed & (1u << lane))) continue;
      uint32_t transitions, accepted, timestamp;
      bool active;
      {
        Guard guard;
        transitions = transitions_[lane];
        accepted = accepted_[lane];
        timestamp = transitionTime_[lane];
        active = (sensorMask_ & (1u << lane)) != 0;
      }
      send("DIAG:SENSOR:%u:%s:%lu:%lu:%lu", (unsigned)lane, active ? "ACTIVE" : "CLEAR",
           (unsigned long)transitions, (unsigned long)accepted, (unsigned long)timestamp);
    }
  }
  void relayState(const char* state, uint32_t now) {
    send("DIAG:RELAY:%u:%s:%02X:%lu", (unsigned)pulseLane_, state, (unsigned)powerMask_, (unsigned long)now);
  }
  void relayPulse(const char* arguments, uint32_t now) {
    if (!diagnostic_) { send("ERR:DIAG_NOT_ACTIVE"); return; }
    if (pulseActive_) { send("ERR:DIAG_RELAY_BUSY"); return; }
    const char* separator = strchr(arguments, ':');
    if (!separator || separator == arguments || size_t(separator - arguments) >= 4) {
      send("ERR:BAD_DIAG_RELAY"); return;
    }
    char laneText[4];
    memcpy(laneText, arguments, size_t(separator - arguments));
    laneText[separator - arguments] = '\0';
    uint32_t lane, duration;
    if (!number(laneText, 10, LaneCount - 1, lane) ||
        !number(separator + 1, 10, MaxRelayPulse, duration) || duration == 0) {
      send("ERR:BAD_DIAG_RELAY"); return;
    }
    lastDiagnosticCommand_ = now;
    pulseActive_ = true;
    pulseLane_ = uint8_t(lane);
    pulseRestoreMask_ = powerMask_;
    pulseStarted_ = now;
    pulseDuration_ = duration;
    setPower(powerMask_ & uint8_t(~(1u << lane)));
    relayState("PULSING", now);
  }
};
}
