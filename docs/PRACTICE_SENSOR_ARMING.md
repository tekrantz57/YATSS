# Practice Sensor Arming

Development feature added October 25, 2026; not in `v0.25.0-beta.3` downloads.
Requires matching controller firmware **0.25.0-capture.1** or later implementing
the command below. Update the app and controller together. This disables
interrupt capture on the controller, not merely counts on the Windows display.

## Use

1. Stop the test before updating firmware. Do not flash while cars circulate
   or track power is enabled.
2. In Practice, with Simulated Lap Input off, open **Mode > Practice Sensor
   Lanes...**. Select only the sensors to test. Lane 8 alone is mask `80`.
   Selecting no lanes is allowed.
3. Apply and wait for the controller acknowledgement. Responding status lists
   the selected lane numbers. If controller recovery is pending, use the usual
   Space-bar continuation after readiness is confirmed.
4. Only selected lanes have normal sensor interrupts attached. Selection lasts
   for this app session, including mode changes/reconnections. Restarting YATSS
   defaults to all configured active lanes.

Changing selection discards queued pre-change edges and resets Practice timing
and sequence baselines after acknowledgement. It preserves totals, best laps
and history. The first subsequent crossing establishes timing without adding a
lap. Reapplying the selection intentionally creates a fresh timing boundary;
retrying the same pending request does not repeatedly reset debounce.

The chooser is unavailable during racing, qualifying, diagnostics or simulated
input. Races/qualifying capture all configured lanes. Returning to Practice
restores its selected subset. Diagnostics intentionally uses all eight inputs,
then restores the selected normal mask on exit with fresh acknowledged timing.

Sensor selection does not change relay output masks. Disarming a sensor does
**not** cut track power or make a lane electrically safe. Normal readiness,
heartbeat/watchdog and explicit recovery requirements still apply.

## Protocol

Protocol-5 checksums and current controller boot-session tags remain required:

```text
CONFIG:CAPTURE:80:1234ABCD:<current-session>*XX
HELLO:CONFIG:CAPTURE:80:1234ABCD:<current-session>*XX
```

The mask is exactly two hex digits, bits 0-7 selecting lanes 1-8. The nonzero
request ID is exactly eight hex digits. A fresh app request identifies each
configuration boundary. The reply must match mask, request ID and current
session before the app trusts physical edges or permits race starts/power-on.
Old replies cannot authorize a later request for the same mask. Unacknowledged
requests retry once per second; status warns that a firmware update may be needed.

The controller detaches interrupts, changes the mask, discards queued normal
edges, clears debounce baselines and reattaches selected interrupts before
acknowledging. Per-lane sequences are retained within the boot session.
The shared capture routine also rejects disabled normal lanes defensively.
The controller defaults to all eight sensors on boot/reset; YATSS reapplies
its desired subset. No selection is written to controller flash.

ESP32 and Pico adapters use selected FALLING interrupts normally and CHANGE
during diagnostics. UNO Q attaches CHANGE interrupts only for selected inputs;
the shared core ignores normal clear transitions. Diagnostic capture remains
independent of the Practice mask and of relay power.

## Validation Limits

Native tests cover masks, invalid/session-bound commands, zero-mask disarming,
queue flushing, sequence continuity, retry idempotence, diagnostics restoration
and reset defaults. Real-app TCP tests cover missing/stale acknowledgements,
selection, rearming baselines and automatic race capture. Each actual adapter
still needs physical interrupt-detachment and mode-change verification.

This helps isolate unused-lane noise, but does not repair grounding, shielding,
biasing, dead-strip contact or conditioning. It does not prove the cause of the
venue's missed laps. External pull-ups and other electrical checks still matter.
