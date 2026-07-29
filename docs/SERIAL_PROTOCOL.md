# YATSS Serial Protocol

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
HELLO:YATSSMC:3:<lane-count>:<board-profile>:<firmware-version>*XX
```

Sent when the sketch starts or when Windows sends `PING`. Version `3` adds the
compile-time board profile and firmware version so Windows can reject a
mismatched firmware update. `YATSSMC` identifies the YATSS microcontroller
firmware. Current board profiles are `ESP32_C6_DEVKITC1` and
`ARDUINO_NANO_ESP32`.

Windows still accepts the version 2 form, `HELLO:YATSSMC:2:<lane-count>`, from
older controllers. Because that form cannot identify the board, an update
requires the operator to confirm the hardware physically.

```text
HEARTBEAT:<millis>*XX
```

Sent about once per second. Windows uses this to detect stale serial
communication and reconnect after known disconnects.

```text
EDGE:<zero-based-lane>:<sequence>:<millis>*XX
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

Error frames are logged by Windows. `ERR:WINDOWS_WATCHDOG` means the controller
received no valid Windows command for five seconds while track power was
enabled, so it cut every lane. Windows uses the timestamp to pause a running
heat or restart the current qualifying attempt.

### Diagnostic Frames

Diagnostic messages are emitted only while a diagnostic session is active:

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

```text
RESET*XX
```

Requests a controller restart. The sketch sends `HELLO:RESETTING*XX`, flushes
serial output, waits briefly, then restarts.

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

This is a communication fail-safe, not a complete electrical fail-safe. With
the documented normally closed relay contacts, loss of controller or relay-coil
power de-energizes the relay and can restore track power. A normally open
contactor, safety relay, or independent hardwired interlock is required if
track power must remain off through controller power loss.
