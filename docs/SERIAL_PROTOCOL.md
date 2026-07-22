# YATSS Serial Protocol

The microcontroller and Windows app communicate over a 115200 baud serial port
using printable ASCII lines. Each line is a protocol body followed by an
optional XOR checksum:

```text
BODY*XX
```

`XX` is the two-digit uppercase hexadecimal XOR of every byte in `BODY`. The
Windows app validates checksums when present. The sketch accepts commands with
or without checksums, but the Windows app sends checksummed commands.

## Controller To Windows

```text
HELLO:LAPS_REDUX:2:<lane-count>*XX
```

Sent when the sketch starts or when Windows sends `PING`. Version `2` means the
controller sends timestamped sensor edges only; Windows owns lap counting.
`LAPS_REDUX` is retained as the protocol identity for compatibility with
existing controllers and tests.

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
ERR:UNKNOWN_COMMAND:<command>*XX
```

Error frames are logged by Windows.

## Windows To Controller

```text
RESET*XX
```

Requests a controller restart. The sketch sends `HELLO:RESETTING*XX`, flushes
serial output, waits briefly, then restarts.

```text
PING*XX
```

Requests a `HELLO:LAPS_REDUX:2:<lane-count>*XX` response.

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
