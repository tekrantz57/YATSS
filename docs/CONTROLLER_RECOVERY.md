# Controller connection loss and recovery

YATSS opens serial and UNO Q TCP connections with track power disabled. Before
offering a start, it requires a supported controller identity, a valid
heartbeat, and acknowledgement of the power-off command. Press Space (or the
R500s Next button) to begin through the normal countdown. Connecting or
reconnecting alone does not enable power, including in Practice mode.

## When communication fails

A transport read/write failure, three seconds without a valid heartbeat, an
explicit controller reset notification, or a backward heartbeat timestamp
interrupts the physical-controller session. Valid sensor frames or unrelated
traffic do not substitute for heartbeats.

- Pending countdowns are cancelled; queued countdown steps and their start
  callbacks cannot subsequently enable power. A phrase already being spoken
  may finish before cancellation takes effect.
- A running heat pauses at the last confirmed controller timestamp. Accepted
  laps, best times, manual adjustments, racers, and heat rotation are retained.
- An interrupted qualifier returns to Ready for a full rerun of that racer's
  attempt. Results from previously completed qualifiers are retained.
- An intermission loses its automatic next-heat start and waits for the
  director. A ready heat remains ready; a paused heat remains paused.
- The main status band identifies the controller problem, and the bottom
  status line reports that YATSS is verifying the controller and power-off
  state. Pressing Space while verification is incomplete cannot start racing.

YATSS requests a power cut whenever communication permits. The existing MCU
watchdog independently cuts power after five seconds without host commands.
The MCU and relay-coil supply must remain powered for that watchdog to operate;
the documented normally closed relay wiring does not fail off on loss of coil
power. Software cannot deliver an immediate relay command through a broken
connection.

## After the controller responds

YATSS reconnects automatically and repeats the power-off/identity/heartbeat
verification. TCP connection attempts have a three-second timeout. The status
band then shows `CONTROLLER READY` and prompts for Space.

Review lap counts and use the existing stopped-heat lap adjustments if needed.
Space starts or resumes the appropriate heat through its countdown, reruns the
interrupted qualifier, starts the next heat after a held intermission, or
restores Practice power. Power is requested at the countdown's `Let's go`
callback. If that write fails, a physical race does not enter Running.

## Timing uncertainty

The host uses a monotonic clock to estimate the displayed running timer between
controller frames. Physical heat/qualifying expiration requires confirmed
controller time. At a fault, the official heat clock freezes at the last
confirmed timestamp, so the display may move back slightly from its prior
estimate. The log records that timestamp and the need to review outage laps.

YATSS cannot know exactly when the car stopped or whether it crossed a sensor
while disconnected. Frames received while recovery is pending are not scored.
For a lane with a pre-interruption timing baseline, the first crossing after
resumption counts without a lap duration and cannot set a fastest lap. The
following complete lap is timed normally. This rule preserves existing lap
history and clears obsolete sequence/baseline state after an MCU reboot.

A backward heartbeat or `HELLO:RESETTING` establishes a fresh controller clock.
Ordinary 32-bit millisecond wrap is handled as forward time. A reconnect to an
MCU whose clock has kept running does not reset its clock. The current protocol
does not carry a boot-session identifier, so a reboot that produces neither a
reset notification nor a backward timestamp cannot be identified conclusively.

## Simulated races

Simulated Lap Input uses its own clock and can run without physical hardware.
Physical-controller failures do not pause a simulated race. Physical power
commands remain subject to the controller verification and power-off policy.

## Verification

Automated tests exercise readiness and explicit resume, lost communication
without a watchdog message, heartbeat deadlines, frozen active time,
post-interruption lap timing, reset sequences, ordinary timestamp wrap,
qualifying reruns, and countdown cancellation.

Before track use, bench-test USB/TCP disconnects and reconnects, MCU resets,
missing heartbeats, and faults during countdowns/intermissions under Windows
and Wine. Confirm actual relay cuts and that power stays off until an explicit
Space restart. See [Publish smoke test](PUBLISH_SMOKE_TEST.md).
