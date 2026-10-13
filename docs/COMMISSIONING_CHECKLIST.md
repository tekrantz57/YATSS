# YATSS Installer Commissioning Checklist

This is an installation test record, not safety certification or proof of
timing accuracy. A demo cannot qualify physical hardware. Use the exact board
pin map and normally open relay wiring guides supplied with this build.
Keep this record with the venue's wiring diagram and track section chart.

Venue: ____________________ Date: __________ Operator: ____________________

App build: ____________________ Controller/firmware: ____________________

OS / Windows or Wine / architecture: ____________________________________

Board model and capacity: ____________________ Active lanes: _____________

Connection (USB serial or UNO Q TCP): ____________________________________

## 1. Before Applying Power

- [ ] Disconnect track and control supplies before wiring changes.
- [ ] Match the physical board, firmware package/profile and pin map. Nano,
      C5, C6 and UNO Q assignments are not interchangeable.
- [ ] Check sensor supply/polarity, shared grounds and MCU input voltage limits
      against the controller guide. Do not apply track/coil voltage to GPIO.
- [ ] Inspect optical alignment, rigid mounting, insulated connections and
      separation of sensor wiring from motor/track feeds.
- [ ] Check every relay: 30 is fused supply, 87 is normally open lane feed,
      87a unused/insulated. Verify actual relay markings, not socket position.
- [ ] Verify the documented driver, flyback suppression and gate pulldown on
      every lane. Record supply, fuse, wiring and relay ratings separately.
- [ ] Provide and test an independent power cutoff. Welded contacts or a
      failed driver cannot be made safe by software alone.

Use RELAY_WIRING.md for driver limitations and board-specific boot checks.
Resolve any mismatch before connecting cars or enabling track power.

## 2. App and Controller Configuration

- [ ] Save Help > Copy Version Information with this record.
- [ ] Set active lane count/names, track length and lap-time limits.
- [ ] Record sensor debounce and raw lockout; check these against intended
      fastest cars. An overly long lockout can discard legitimate crossings.
- [ ] Disable Simulated Lap Input. Identify the physical controller and verify
      heartbeats, power-off acknowledgement and CONTROLLER READY.
- [ ] For Wine USB, confirm the COM mapping and host permissions. For UNO Q,
      verify the controller app/bridge starts as intended and TCP reconnects.
- [ ] Select speech backend or None; verify it does not block racing. Test
      announcements and lap-best sounds if enabled.
- [ ] Choose export options and verify writable report/settings/backup locations.

Minimum lap (ms): ______ Debounce (ms): ______ Raw lockout (ms): ______

Track length: ______ Speech backend: ______ JSON / CSV enabled: ______

## 3. Sensors, Lane by Lane

Use File > Controller Diagnostics with track power disconnected. Verify raw
state, transition counts and accepted edges. Record differences from a baseline.

| Lane | Pin verified | Clear/blocked | Single crossing | Other lanes unaffected | Moving car |
| --- | --- | --- | --- | --- | --- |
| 1 | | | | | |
| 2 | | | | | |
| 3 | | | | | |
| 4 | | | | | |
| 5 | | | | | |
| 6 | | | | | |
| 7 | | | | | |
| 8 | | | | | |

- [ ] Test actual guide flags/materials, slow and fast passages, and repeatable
      moving-car detection. A hand interruption alone is insufficient.
- [ ] Test simultaneous crossings on all active lanes; reconcile every count.
- [ ] Verify minimum-lap rejection and sensor lockout without losing valid laps.
- [ ] Distinguish ordinary practice/heat baseline crossings from Combined
      Distance starting-line timing. Test departure pulses and first full laps.

## 4. Track Power and Failures

Use controlled tests with no cars initially. Diagnostics pulses are cut-only:
they do not establish that a lane can be energized. Test power-on through the
normal verified start workflow and record each lane independently.

| Lane | Boot off | Start on | Track-call off | Correct lane only | Reset/loss off |
| --- | --- | --- | --- | --- | --- |
| 1 | | | | | |
| 2 | | | | | |
| 3 | | | | | |
| 4 | | | | | |
| 5 | | | | | |
| 6 | | | | | |
| 7 | | | | | |
| 8 | | | | | |

- [ ] Test app startup, MCU reset, controller-power loss, coil-supply loss and
      restoration. Healthy normally open contacts remain off until authorized.
- [ ] Break USB/TCP communication while keeping the MCU powered; verify watchdog
      cutoff and stopped racing. Reconnect must not automatically start.
- [ ] Check countdown, heat-end and track-call power behavior; measure actual
      switching latency and verify prolonged driver/coil operation.
- [ ] Test the independent cutoff. Record faults and do not proceed with an
      unexplained powered lane, missed cutoff or unintended restart.

## 5. Small Physical Race and Recovery

- [ ] Run practice with real cars; compare lap counts/times to an independent
      reference. Check noise while motors operate.
- [ ] Run a short heat race with at least one lane rotation and track call.
      Verify racer identity, carried totals, heat duration and intermission.
- [ ] Verify a manual correction and current-heat rerun; prior heats remain,
      power stays off, cars return to their heat-start positions before Space.
- [ ] If using Combined Distance, test qualifying credit, lane choices,
      equivalent-position transfers, group finish approvals and final totals.
      Prepare a track-section-to-hundredths chart; estimates are not official.
- [ ] Test interrupted-event recovery in a noncompetitive session. Review the
      last saved time, counts and car positions; reconcile uncertain crossings.
- [ ] Complete the event and verify report/exports and a settings backup/restore
      using copies. Settings backups do not include the active-event journal.
- [ ] Run a sustained physical session. Record missed frames, rejected edges,
      communication interruptions and any count discrepancies.

Results / discrepancies: __________________________________________________

Unresolved issues: ________________________________________________________

Installer review: __________________ Venue review: ________________________

Keep failures visible; completion of boxes is not a blanket approval for venue
competition. The longer PUBLISH_SMOKE_TEST.md covers release/developer checks.
Use Help > Report a Bug for reproducible software faults. Remove private racer
data, connection addresses and personal paths from shared logs and screenshots.
