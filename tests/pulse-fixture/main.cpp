#include <cassert>
#include <cstdint>
#include <cstdio>
#include "../../YATSSPulseTest/RmtPulseTrain.h"

struct Fake {
  uint32_t clock = 0;
  bool prepared = false, finished = false;
  DragPulse::PulsePlan plan;
  uint32_t now() { return clock; }
  void set(uint8_t, bool) {}
  bool prepare(const DragPulse::PulsePlan& p, uint8_t channel) { plan = p; prepared = channel == 0; return prepared; }
  bool fire() { finished = false; return prepared; }
  bool done() { return finished; }
  void release() { prepared = false; }
};

int main() {
  DragPulse::PulsePlan plan;
  assert(plan.build(1000000, true) && plan.idleHigh && plan.length <= 34);
  uint32_t width = 0;
  for (uint8_t i = 0; i < plan.length; ++i) if (!plan.halves[i].high) width += plan.halves[i].ticks;
  assert(width == 1000000);
  assert(plan.build(1000, false) && !plan.idleHigh && plan.halves[0].high);
  assert(!plan.build(99, true));
  Fake io;
  DragPulse::RmtTrain<Fake> train(io);
  DragPulse::Config config{1000, 3000000, 2, 997, 1};
  assert(train.begin(config, true));
  io.clock = 1000000; train.tick();
  io.clock += 1000; io.finished = true; train.tick();
  assert(train.completed == 1 && train.running);
  assert(train.requestedGaps.minimum >= 3000000 && train.requestedGaps.maximum <= 3000997);
  io.clock += train.requestedGaps.minimum; train.tick();
  io.clock += 1000; io.finished = true; train.tick();
  assert(train.completed == 2 && !train.running);
  assert(train.begin(config, false));
  io.clock += 1000000; train.tick();
  train.stop();
  assert(train.completed == 0 && !train.running && !io.prepared);
  config.mask = 3;
  assert(!train.begin(config, true));
  std::puts("Pulse fixture plan, timing, completion, stop and polarity checks passed.");
}
