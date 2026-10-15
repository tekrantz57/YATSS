# Controller connection loss and recovery

For application closing, crashes, and OS restarts, see
[Active event recovery](ACTIVE_RACE_RECOVERY.md). Both recovery paths hold
power off until the director explicitly resumes.

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
current normally open contacts release on coil-power loss; controller power-loss
behavior also depends on the external gate pulldown and verified GPIO behavior.
See [Relay wiring](RELAY_WIRING.md). Software cannot deliver an immediate relay command through a broken
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

Protocol 5 carries a random 64-bit **controller timing-session identifier** on
every controller response. It changes at MCU startup and on `RESET`, including
the UNO Q's logical reset that keeps MCU uptime and RouterBridge alive. A new
identity pauses physical racing even if no reset message was received and the
new timestamp is larger than the previous one. Identity, heartbeat, and power-off
acknowledgement must all belong to the verified session before explicit resume.

Session history is retained across USB/TCP reconnects for the lifetime of YATSS.
A reconnect to the same session does not reset its clock; it still requires a
fresh readiness handshake and director authorization. Replies from retired
sessions are discarded before clock, readiness, diagnostics, or scoring updates.
An unfamiliar session on an edge or heartbeat pauses racing and requests identity;
only a valid protocol-5 identity can establish the new session.

`HELLO:RESETTING` immediately retires the old session. Clock rollback remains an
additional fault check, and ordinary 32-bit millisecond wrap remains forward time.
Power-enable and relay-pulse commands carry the verified session, so delayed
commands cannot energize a newly restarted controller. Discovery and power-off
commands work without a session. This identifier is not authentication, persistent
storage, or recovery of missed crossings. Update the app and firmware together;
older physical firmware cannot authorize track power in this app.

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
