# YATSS Serial Protocol

Development firmware `0.25.0-capture.1` adds session-bound
`CONFIG:CAPTURE:<two-hex-mask>:<eight-hex-request-id>` and its matching
`HELLO:CONFIG:CAPTURE` acknowledgement. It controls sensor interrupts, never
relay outputs. See [Practice sensor arming](PRACTICE_SENSOR_ARMING.md) for
request ordering, baseline/queue behavior and diagnostic overrides.

The microcontroller and Windows app communicate over a 115200 baud serial port
using printable ASCII lines. Every line is a protocol body followed by a
required XOR checksum:

```text
BODY*XX
```

`XX` is the two-digit uppercase hexadecimal XOR of every byte in `BODY`.
Windows and the controller reject lines with a missing, malformed, or incorrect
checksum.

## Controller To Windows

```text
HELLO:YATSSMC:5:<lane-count>:<board-profile>:<firmware-version>:<flash-bytes>:<session>*XX
```

Sent when the sketch starts or when Windows sends `PING`. Version `5` reports
the compile-time board profile, firmware version, and runtime-detected flash
capacity so Windows can reject a mismatched firmware update. `YATSSMC`
identifies the YATSS microcontroller firmware. Current board profiles are
`ESP32_C5_WAVESHARE_WIFI6_N16R8`, `ESP32_C6_DEVKITC1`, and
`ARDUINO_NANO_ESP32`, and `ARDUINO_UNO_Q_STM32U585`.

`session` is a nonzero 16-digit uppercase hexadecimal identifier generated from
hardware-backed randomness at boot and on RESET. Every controller-to-app frame
ends in `:<session>` before the checksum, which covers that suffix too. Examples
below abbreviate the suffix only where explicitly stated. It remains unchanged
across PING, heartbeat, USB reconnect, and Linux bridge restart. A logical UNO Q
RESET changes the identifier even though MCU uptime is retained. If entropy
generation fails, the controller emits `ERR:SESSION_ENTROPY`, uses the invalid
all-zero identifier for diagnostics, and refuses to energize power.

Physical racing requires protocol 5. The parser can still read untagged demo
frames and older identity formats for inspection/tests, but these cannot establish
physical-controller readiness. Initial firmware-update probes may identify an
older controller before replacing it with the current firmware.

```text
HEARTBEAT:<millis>:<session>*XX
```

Sent about once per second. Windows uses this to detect stale serial
communication and reconnect after known disconnects.

```text
EDGE:<zero-based-lane>:<sequence>:<millis>:<session>*XX
```

Sent when a sensor input sees a debounced falling edge. Lanes are zero-based:
lane `0` is the first Windows lane, lane `1` is the second, and so on. The
sequence is per lane and is used by Windows to detect stale frames after a
controller reset. The timestamp is the controller `millis()` value captured in
the interrupt.

```text
ERR:QUEUE_FULL:<dropped-count>*XX
ERR:BAD_CHECKSUM*XX
ERR:BAD_POWER_MASK*XX
ERR:BAD_DEBOUNCE*XX
ERR:DIAG_NOT_ACTIVE*XX
ERR:DIAG_RELAY_BUSY*XX
ERR:BAD_DIAG_RELAY*XX
ERR:WINDOWS_WATCHDOG:<millis>*XX
ERR:UNKNOWN_COMMAND:<command>*XX
```

All error examples above also carry `:<session>` immediately before `*XX`.
`ERR:STALE_SESSION` means a tagged command named a retired/wrong session, or an
energizing command omitted the current session. Rejected commands do not refresh
the watchdog. Error frames are logged by Windows. `ERR:WINDOWS_WATCHDOG` means the controller
received no valid Windows command for five seconds while track power was
enabled, so it cut every lane. Windows uses the timestamp to pause a running
heat or restart the current qualifying attempt.

### Diagnostic Frames

Diagnostic messages are emitted only while a diagnostic session is active:
Every diagnostic example below carries `:<session>` before `*XX`, just like
power acknowledgements and other HELLO responses.

```text
DIAG:SESSION:STARTED:<millis>*XX
DIAG:SESSION:STOPPED:<reason>:<millis>*XX
DIAG:STATUS:<sensor-mask>:<power-mask>:<debounce>:<dropped>:<millis>*XX
DIAG:SENSOR:<lane>:<ACTIVE|CLEAR>:<transition-count>:<accepted-edge-count>:<millis>*XX
DIAG:RELAY:<lane>:<PULSING|RESTORED>:<power-mask>:<millis>*XX
```

The two masks are two-digit hexadecimal values. A set sensor bit means the
active-low input is currently low. A set power bit means track power is enabled
for that lane. Sensor transition counts include both active and clear changes;
accepted-edge counts include active transitions that pass the configured
controller debounce. `dropped` is the cumulative controller edge-queue
overflow count since boot.

## Windows To Controller

YATSS appends `:<session>` to state-changing commands once identity is known.
In the examples below, that suffix is omitted for readability. It is mandatory
for `TRACK_POWER:ON`, nonzero `TRACK_POWER:MASK`, and `DIAG:RELAY:PULSE`.
The controller rejects incorrect tagged sessions. `PING`, `KEEPALIVE`,
`TRACK_POWER:OFF`, and `TRACK_POWER:MASK:00` remain usable without a suffix;
power-off commands are honored even if a supplied session is stale. This lets
the app cut power after an unexpected reset before learning the new identity.
The identifier prevents accidental stale traffic, not malicious control.

```text
RESET*XX
```

Requests a timing-session restart. The sketch sends `HELLO:RESETTING:<old-session>*XX`,
cuts power and clears queued timing state. ESP32 then reboots; UNO Q rotates the
session and resets timing state without restarting RouterBridge or MCU uptime.

```text
PING*XX
```

Requests the current `HELLO:YATSSMC` response.

```text
KEEPALIVE*XX
```

Acknowledges a controller heartbeat. Windows sends this once per real
controller heartbeat. Any valid Windows command refreshes the same five-second
communication watchdog; `KEEPALIVE` exists so idle race-control periods remain
armed without changing another setting.

```text
TRACK_POWER:OFF*XX
TRACK_POWER:ON*XX
```

Cuts or restores power on all configured track-power outputs.

```text
TRACK_POWER:MASK:<hex-mask>*XX
```

Sets enabled lanes with an 8-bit hexadecimal mask. Bit `0` controls lane `0`.
An enabled bit restores track power for that lane; a cleared bit cuts it.

```text
CONFIG:DEBOUNCE:<milliseconds>*XX
```

Sets the controller-side sensor-edge debounce. The value must be `0` through
`10000`. The sketch echoes the accepted value as:

```text
HELLO:CONFIG:DEBOUNCE:<milliseconds>*XX
```

Windows also has its own minimum lap time and raw edge lockout. The controller
debounce reduces serial traffic; Windows remains the authority for lap
validation and counting.

### Diagnostic Commands

```text
DIAG:START*XX
DIAG:STOP*XX
DIAG:STATUS*XX
DIAG:CLEAR*XX
DIAG:RELAY:PULSE:<zero-based-lane>:<milliseconds>*XX
```

`DIAG:START` suppresses normal `EDGE` frames and changes the sensor interrupts
to report both signal transitions. `DIAG:CLEAR` resets the per-lane transition
counts. Relay pulses must be from 1 through 2000 milliseconds and can only cut
power; the controller restores the previous power mask when the pulse expires.
Only one relay pulse can run at a time. Any explicit track-power command cancels
an active pulse and becomes authoritative, preventing a delayed restoration.

Windows sends `DIAG:STATUS` once per second as the session keepalive. The
controller stops diagnostics after five seconds without a diagnostic command,
restores any active relay pulse, and returns sensor interrupts to normal lap
timing mode.

## Track-Power Fail-Safe Policy

The controller configures every track-power GPIO to the cut level before
starting serial communication or waiting through its startup delay. Once valid
Windows traffic has been received, a five-second command watchdog is armed. If
the watchdog expires while any lane is enabled, the controller cancels any
diagnostic relay pulse, cuts all lanes, and reports `ERR:WINDOWS_WATCHDOG`.

The firmware never restores power merely because communication resumes. A
subsequent track-power command from Windows is required. When Windows receives
the watchdog report, it pauses a running heat; an interrupted qualifier is
returned to Ready so the same racer can rerun it.

This is a communication fail-safe, not a complete electrical safety system.
Current normally open contacts release when the coil loses power, with an
external gate pulldown for high-impedance controller outputs.
See [Relay wiring](RELAY_WIRING.md).
Welded contacts and shorted drivers still require independent protection.
