#pragma once

// The driver may retain the payload after a timed-out wait. Keep it alive for
// the fixture's lifetime and prohibit reuse once completion is uncertain.
template<class Symbol>
class ManualLevel {
 public:
  bool healthy() const { return !faulted_; }

  template<class Submit, class Wait, class Abort>
  bool set(bool high, Submit submit, Wait wait, Abort abort) {
    if (faulted_) return false;
    symbol_.val = 0;
    symbol_.level0 = high; symbol_.duration0 = 1;
    symbol_.level1 = high; symbol_.duration1 = 1;
    if (!submit(&symbol_)) return false;
    if (wait()) return true;
    faulted_ = true;
    abort();
    return false;
  }
 private:
  Symbol symbol_ = {};
  bool faulted_ = false;
};
