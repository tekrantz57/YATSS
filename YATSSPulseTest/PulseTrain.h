#pragma once

#include <stdint.h>

namespace DragPulse {

struct Config {
  uint32_t widthUs, gapUs, count, jitterUs;
  uint8_t mask;
};

inline bool valid(const Config& c) {
  return c.widthUs >= 100 && c.widthUs <= 1000000 &&
         c.gapUs >= 100 && c.gapUs <= 60000000 &&
         c.count >= 1 && c.count <= 1000000 &&
         c.jitterUs <= 1000000 && c.mask >= 1 && c.mask <= 15;
}

struct Range {
  uint32_t minimum = UINT32_MAX, maximum = 0;
  void add(uint32_t value) {
    if (value < minimum) minimum = value;
    if (value > maximum) maximum = value;
  }
};

// IO supplies now() and set(channel, blocked). All arithmetic is wrap-safe
// provided tick() is serviced at least once every 2^31 microseconds.
template <class IO>
class Train {
 public:
  explicit Train(IO& io) : io_(io) {}
  bool running = false, blocked = false;
  uint32_t completed = 0, maxLateUs = 0;
  Range widths[4], gaps[4];

  bool begin(const Config& config) {
    if (!valid(config) || running) return false;
    config_ = config;
    completed = maxLateUs = 0;
    random_ = 0x5EED1234UL;
    for (uint8_t i = 0; i < 4; ++i) {
      widths[i] = Range();
      gaps[i] = Range();
      io_.set(i, false);
    }
    blocked = false;
    running = true;
    deadline_ = io_.now() + 1000000UL;
    return true;
  }

  void stop() {
    for (uint8_t i = 0; i < 4; ++i) io_.set(i, false);
    blocked = running = false;
  }

  void tick() {
    if (!running) return;
    const uint32_t now = io_.now();
    if (static_cast<int32_t>(now - deadline_) < 0) return;
    const uint32_t late = now - deadline_;
    if (late > maxLateUs) maxLateUs = late;
    blocked = !blocked;
    for (uint8_t i = 0; i < 4; ++i) {
      if (!(config_.mask & (1U << i))) continue;
      io_.set(i, blocked);
      const uint32_t stamp = io_.now();
      if (blocked) {
        if (completed) gaps[i].add(stamp - lastClear_[i]);
        lastBlock_[i] = stamp;
      } else {
        widths[i].add(stamp - lastBlock_[i]);
        lastClear_[i] = stamp;
      }
    }
    if (!blocked && ++completed == config_.count) {
      running = false;
      return;
    }
    // Schedule from the completed transition: never compress late pulses to
    // catch up. Channels are near-simultaneous digitalWrites, not atomic edges.
    deadline_ = io_.now() + (blocked ? config_.widthUs : nextGap());
  }

 private:
  uint32_t nextGap() {
    random_ ^= random_ << 13;
    random_ ^= random_ >> 17;
    random_ ^= random_ << 5;
    return config_.gapUs + random_ % (config_.jitterUs + 1);
  }
  IO& io_;
  Config config_ = {};
  uint32_t deadline_ = 0, random_ = 0;
  uint32_t lastBlock_[4] = {}, lastClear_[4] = {};
};

}  // namespace DragPulse
