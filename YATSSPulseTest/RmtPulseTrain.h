#pragma once

#include "PulseTrain.h"

namespace DragPulse {

struct PulseHalf {
  uint16_t ticks;
  bool high;
};

struct PulsePlan {
  PulseHalf halves[34] = {};
  uint8_t length = 0;
  bool idleHigh = false;

  bool build(uint32_t widthUs, bool inverted) {
    length = 0;
    idleHigh = inverted;
    if (widthUs < 100 || widthUs > 1000000) return false;
    while (widthUs) {
      const uint16_t part = widthUs > 32767 ? 32767 : static_cast<uint16_t>(widthUs);
      halves[length++] = {part, !idleHigh};
      widthUs -= part;
    }
    // RMT symbols have two nonzero half-durations. End every transaction
    // with two microseconds of clear level, then hold clear indefinitely.
    if (length & 1) halves[length++] = {2, idleHigh};
    else {
      halves[length++] = {1, idleHigh};
      halves[length++] = {1, idleHigh};
    }
    return true;
  }
};

inline bool singleChannel(uint8_t mask) {
  return mask && mask <= 8 && !(mask & (mask - 1));
}

// Transport supplies prepare(plan, channel), fire(), done(), release(),
// now() and set(channel, blocked). Only gaps and first-start delay use software.
template <class Transport>
class RmtTrain {
 public:
  explicit RmtTrain(Transport& io) : io_(io) {}
  bool running = false, failed = false;
  uint32_t completed = 0, maxLateUs = 0;
  Range rearmWait, requestedGaps;

  bool begin(const Config& config, bool inverted) {
    if (running || !valid(config) || !singleChannel(config.mask)) return false;
    failed = false;
    completed = maxLateUs = 0;
    rearmWait = Range();
    requestedGaps = Range();
    config_ = config;
    random_ = 0x5EED1234UL;
    uint8_t channel = 0;
    while (!(config.mask & (1U << channel))) ++channel;
    PulsePlan plan;
    plan.build(config.widthUs, inverted);
    for (uint8_t i = 0; i < 4; ++i) io_.set(i, false);
    if (!io_.prepare(plan, channel)) {
      failed = true;
      stop();
      return false;
    }
    active_ = false;
    running = true;
    deadline_ = io_.now() + 1000000UL;
    return true;
  }

  void stop() {
    // Count only transactions whose completion the driver has confirmed.
    if (active_ && io_.done()) ++completed;
    active_ = running = false;
    io_.release();
    for (uint8_t i = 0; i < 4; ++i) io_.set(i, false);
  }

  void tick() {
    if (!running) return;
    const uint32_t now = io_.now();
    if (active_) {
      if (!io_.done()) {
        if (now - submittedAt_ > config_.widthUs + 2000000UL) {
          failed = true;
          stop();
        }
        return;
      }
      active_ = false;
      ++completed;
      if (completed == config_.count) {
        stop();
        return;
      }
      clearObservedAt_ = now;
      random_ ^= random_ << 13;
      random_ ^= random_ >> 17;
      random_ ^= random_ << 5;
      const uint32_t gap = config_.gapUs + random_ % (config_.jitterUs + 1);
      requestedGaps.add(gap);
      deadline_ = now + gap;
      return;
    }
    if (static_cast<int32_t>(now - deadline_) < 0) return;
    const uint32_t late = now - deadline_;
    if (late > maxLateUs) maxLateUs = late;
    if (completed) rearmWait.add(now - clearObservedAt_);
    if (!io_.fire()) {
      failed = true;
      stop();
      return;
    }
    submittedAt_ = now;
    active_ = true;
  }

 private:
  Transport& io_;
  Config config_ = {};
  bool active_ = false;
  uint32_t deadline_ = 0, submittedAt_ = 0, clearObservedAt_ = 0, random_ = 0;
};

}  // namespace DragPulse
