#include "../../Controller/YatssController.h"
#include <algorithm>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

static unsigned assertions = 0;
static void check(bool condition, const char* message) {
  ++assertions;
  if (!condition) throw std::runtime_error(message);
}

struct Hardware {
  static uint32_t clock;
  static uint8_t power, sensors;
  static unsigned locks, resets;
  static bool diagnostics;
  static std::vector<std::string> frames;
  static std::vector<uint8_t> powerHistory;
  static uint32_t now() { return clock; }
  static const char* profile() { return "HOST_TEST"; }
  static uint32_t flashBytes() { return 8388608; }
  static void lock() { ++locks; }
  static void unlock() { check(locks == 1, "critical sections must not nest"); --locks; }
  static void reset() { ++resets; }
  static void configureCapture(bool value) { diagnostics = value; }
  static uint8_t sensorMask() { return sensors; }
  static void setPower(uint8_t mask) {
    power = mask;
    powerHistory.push_back(mask);
    for (uint8_t lane = 0; lane < 8; ++lane)
      check(yatss::trackPowerOutputHigh(mask, lane) == ((mask & (1u << lane)) != 0),
            "normally open relay must energize only an enabled lane");
  }
  static void sendFrame(const char* frame) {
    check(locks == 0, "transport output must not run inside critical section");
    std::string text(frame);
    size_t marker = text.rfind('*');
    check(marker != std::string::npos && text.size() == marker + 3, "frame must carry two hex checksum digits");
    unsigned expected = unsigned(std::stoul(text.substr(marker + 1), nullptr, 16));
    std::string body = text.substr(0, marker);
    check(expected == yatss::Controller<Hardware, 32>::checksum(body.c_str()), "output checksum must match");
    frames.push_back(body);
  }
  static void clear() {
    clock = 0; power = 0xFF; sensors = 0; locks = resets = 0; diagnostics = false;
    frames.clear(); powerHistory.clear();
  }
};
uint32_t Hardware::clock = 0;
uint8_t Hardware::power = 0, Hardware::sensors = 0;
unsigned Hardware::locks = 0, Hardware::resets = 0;
bool Hardware::diagnostics = false;
std::vector<std::string> Hardware::frames;
std::vector<uint8_t> Hardware::powerHistory;

static std::string encode(const std::string& body) {
  char suffix[4];
  snprintf(suffix, sizeof(suffix), "*%02X", unsigned(yatss::Controller<Hardware, 32>::checksum(body.c_str())));
  return body + suffix;
}
static bool seen(const std::string& body) {
  return std::find(Hardware::frames.begin(), Hardware::frames.end(), body) != Hardware::frames.end();
}
static size_t countPrefix(const std::string& prefix) {
  return size_t(std::count_if(Hardware::frames.begin(), Hardware::frames.end(), [&](const std::string& frame) {
    return frame.compare(0, prefix.size(), prefix) == 0;
  }));
}

template<uint8_t QueueSize>
static void run() {
  using Core = yatss::Controller<Hardware, QueueSize>;
  for (unsigned mask = 0; mask < 256; ++mask) {
    for (uint8_t lane = 0; lane < 8; ++lane)
      check(yatss::trackPowerOutputHigh(uint8_t(mask), lane) == ((mask & (1u << lane)) != 0),
            "all power masks must map to active-high run outputs");
    check(!yatss::trackPowerOutputHigh(uint8_t(mask), 8), "invalid relay lane must stay off");
  }
  Hardware::clear();
  Core core;
  auto command = [&](const std::string& body) { core.command(encode(body).c_str()); };
  auto capture = [&](uint8_t lane, bool active, uint32_t now) {
    Hardware::lock(); core.capture(lane, active, now); Hardware::unlock();
  };
  core.begin();
  check(Hardware::power == 0 && !core.watchdogArmed(), "boot must cut all lanes without arming watchdog");
  check(seen(std::string("HELLO:YATSSMC:4:8:HOST_TEST:") + YATSSMC_FIRMWARE_VERSION + ":8388608"), "hello identifies firmware and capacity");
  check(Core::checksum("KEEPALIVE") == 0x4C, "existing keepalive checksum must stay unchanged");
  capture(0, true, 0);
  capture(0, true, 100); // A baseline at timestamp zero must still debounce.
  capture(0, false, 200);
  capture(0, true, 1800);
  core.service();
  check(countPrefix("EDGE:0:") == 2, "default debounce suppresses bounce and clear edges");
  check(seen("EDGE:0:1:0") && seen("EDGE:0:2:1800"), "accepted edges retain ISR timestamps and sequence");
  command("CONFIG:DEBOUNCE:0");
  for (uint8_t lane = 1; lane < 8; ++lane) capture(lane, true, 2000 + lane);
  core.service();
  for (uint8_t lane = 1; lane < 8; ++lane) {
    check(seen("EDGE:" + std::to_string(lane) + ":1:" + std::to_string(2000 + lane)), "all eight lanes must share edge behavior");
  }
  command("CONFIG:DEBOUNCE:10001");
  check(seen("ERR:BAD_DEBOUNCE"), "debounce beyond maximum must be rejected");
  command("CONFIG:DEBOUNCE:-1");
  command("CONFIG:DEBOUNCE:");
  command("CONFIG:DEBOUNCE:429496729600");
  command("TRACK_POWER:MASK:1G");
  check(seen("ERR:BAD_POWER_MASK") && Hardware::power == 0, "malformed masks must not enable power");
  command("TRACK_POWER:MASK:81");
  check(Hardware::power == 0x81, "mask must select exact lanes");
  command("TRACK_POWER:ON");
  check(Hardware::power == 0xFF, "ON must enable all lanes");
  Hardware::clock = 4999;
  core.service();
  check(Hardware::power == 0xFF, "watchdog must not expire early");
  core.command("KEEPALIVE*00");
  check(seen("ERR:BAD_CHECKSUM"), "invalid checksum must be rejected");
  Hardware::clock = 5000;
  core.service();
  check(Hardware::power == 0 && core.problem() && !core.watchdogArmed(), "bad checksum must not keep track power alive");
  check(seen("ERR:WINDOWS_WATCHDOG:5000"), "watchdog must identify exact expiry timestamp");
  command("KEEPALIVE");
  check(!core.problem() && core.watchdogArmed(), "valid command should recover communications indication");
  command("DIAG:RELAY:PULSE:0:500");
  check(seen("ERR:DIAG_NOT_ACTIVE"), "relay test requires diagnostics");
  Hardware::sensors = 2;
  command("DIAG:START");
  check(Hardware::diagnostics && seen("DIAG:STATUS:02:00:0:0:5000"), "diagnostics must report raw sensor and power state");
  capture(2, true, 5000);
  capture(2, false, 5001);
  core.service();
  check(seen("DIAG:SENSOR:2:CLEAR:2:1:5001"), "diagnostics coalesces output while preserving transition counts");
  check(!seen("EDGE:2:2:5000"), "diagnostic transitions must not become race edges");
  command("CONFIG:DEBOUNCE:1800");
  command("DIAG:CLEAR");
  capture(2, true, 6000);
  capture(2, false, 6010);
  capture(2, true, 6100);
  core.service();
  check(seen("DIAG:SENSOR:2:ACTIVE:3:1:6100"), "diagnostics applies common debounce and resets counters");
  command("TRACK_POWER:ON");
  command("DIAG:RELAY:PULSE:0:500");
  check(Hardware::power == 0xFE, "relay pulse cuts just its selected lane");
  command("DIAG:RELAY:PULSE:1:500");
  check(seen("ERR:DIAG_RELAY_BUSY"), "overlapping relay pulses must be rejected");
  Hardware::clock = 5500;
  core.service();
  check(Hardware::power == 0xFF && seen("DIAG:RELAY:0:RESTORED:FF:5500"), "pulse restores previous mask at deadline");
  command("DIAG:RELAY:PULSE:0:500");
  command("TRACK_POWER:OFF");
  Hardware::clock = 6000;
  core.service();
  check(Hardware::power == 0, "OFF must cancel a pending pulse restore");
  command("TRACK_POWER:ON");
  command("DIAG:RELAY:PULSE:7:2000");
  command("TRACK_POWER:MASK:03");
  Hardware::clock = 8000;
  core.service();
  check(Hardware::power == 3, "mask command must supersede pulse restore");
  command("DIAG:RELAY:PULSE:8:10");
  command("DIAG:RELAY:PULSE:0:0");
  command("DIAG:RELAY:PULSE:0:2001");
  command("DIAG:RELAY:PULSE:0:-1");
  check(seen("ERR:BAD_DIAG_RELAY"), "relay bounds must be enforced");
  Hardware::clock = 11000;
  command("KEEPALIVE");
  core.service();
  check(!Hardware::diagnostics && seen("DIAG:SESSION:STOPPED:TIMEOUT:11000"), "ordinary keepalive must not extend diagnostics indefinitely");
  command("DIAG:START");
  command("TRACK_POWER:ON");
  Hardware::clock = 14000;
  command("DIAG:STATUS");
  // A delayed service call must process watchdog before restoring an overdue pulse.
  command("DIAG:RELAY:PULSE:0:2000");
  size_t powerChanges = Hardware::powerHistory.size();
  Hardware::clock = 19000;
  core.service();
  check(Hardware::power == 0, "watchdog and diagnostic timeout must leave power cut");
  check(std::all_of(Hardware::powerHistory.begin() + powerChanges, Hardware::powerHistory.end(),
                   [](uint8_t mask) { return mask == 0; }), "watchdog must cancel overdue relay restoration before any power-on write");
  command("RESET");
  check(Hardware::resets == 1 && !core.watchdogArmed() && !core.problem() && Hardware::power == 0, "reset cuts power and resets controller session");
  capture(0, true, 19000);
  core.service();
  check(seen("EDGE:0:1:19000"), "reset restarts per-lane sequences without needing MCU uptime reset");
  command("CONFIG:DEBOUNCE:0");
  for (unsigned index = 0; index < QueueSize + 5u; ++index) capture(0, true, 20000 + index);
  core.service();
  check(seen("ERR:QUEUE_FULL:6") && core.problem(), "ring queue preserves one empty slot and reports overflow");
  command("PING");
  check(core.problem(), "queue problem remains latched until reset");
  command("RESET");
  check(!core.problem(), "reset clears queue warning");

  Hardware::clear();
  Core wrapping;
  Hardware::clock = UINT32_MAX - 1000;
  auto wrapCommand = [&](const std::string& body) { wrapping.command(encode(body).c_str()); };
  auto wrapCapture = [&](uint32_t now) { Hardware::lock(); wrapping.capture(0, true, now); Hardware::unlock(); };
  wrapping.begin();
  wrapCapture(UINT32_MAX - 1000);
  wrapCapture(100);
  wrapCapture(1000);
  wrapping.service();
  check(countPrefix("EDGE:0:") == 2 && seen("EDGE:0:2:1000"), "debounce must handle uint32 wrap");
  wrapCommand("TRACK_POWER:ON");
  Hardware::clock = 3998;
  wrapping.service();
  check(Hardware::power == 0xFF, "watchdog wrap deadline must not fire early");
  Hardware::clock = 3999;
  wrapping.service();
  check(Hardware::power == 0, "watchdog must expire across uint32 wrap");
  Hardware::clock = UINT32_MAX - 99;
  wrapCommand("DIAG:START");
  wrapCommand("TRACK_POWER:ON");
  wrapCommand("DIAG:RELAY:PULSE:0:200");
  Hardware::clock = 99;
  wrapping.service();
  check(Hardware::power == 0xFE, "relay wrap pulse must not restore early");
  Hardware::clock = 100;
  wrapping.service();
  check(Hardware::power == 0xFF, "relay pulse must restore across wrap");
  check(Hardware::locks == 0, "all critical sections must balance");

  Hardware::clear();
  Core modes;
  modes.begin();
  Hardware::lock(); modes.capture(0, true, 0); Hardware::unlock();
  modes.command(encode("DIAG:START").c_str());
  modes.service();
  check(countPrefix("EDGE:") == 0, "queued racing edges must not cross into diagnostics");
  Hardware::lock(); modes.capture(1, true, 100); Hardware::unlock();
  modes.command(encode("DIAG:STOP").c_str());
  modes.service();
  check(countPrefix("DIAG:SENSOR:") == 0, "pending diagnostic display updates must not cross back into racing");
  Hardware::lock(); modes.capture(0, true, 101); Hardware::unlock();
  modes.service();
  check(seen("EDGE:0:2:101"), "leaving diagnostics clears debounce baseline without resetting sequence");
  Hardware::clock = 999;
  modes.service();
  check(countPrefix("HEARTBEAT:") == 0, "heartbeat must not be published before its interval");
  Hardware::clock = 1000;
  modes.service();
  check(seen("HEARTBEAT:1000"), "heartbeat must be published at deadline");
  Hardware::clock = 1999;
  modes.service();
  check(countPrefix("HEARTBEAT:") == 1, "heartbeat interval must restart after publication");
}

int main() {
  try {
    run<32>();
    run<64>();
    std::cout << "Shared controller tests passed (" << assertions << " checks).\n";
    return 0;
  } catch (const std::exception& error) {
    std::cerr << error.what() << '\n';
    return 1;
  }
}
