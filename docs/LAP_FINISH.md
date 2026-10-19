# Lap Finish Racing

Available in current development source after `v0.25.0-beta.3`; not yet in that
release's downloads. This is a simple race-to-laps format, not a claim of
compliance with any particular organization's or country's rules.

## Set Up and Run

1. Open `Mode > Heat Race...`. For a ready-to-run hardware-free example, choose
   `Mode > Lap Finish Demo...` instead: it supplies one demo racer per active
   lane, a 10-lap target, and simulated input. Press Space to start.
   `Demo Race...` remains the preset timed heat demonstration. For custom
   hardware-free settings, enable `Mode > Simulated Lap Input` before setup.
2. Select **Lap Finish** in **Race format**.
3. Enter **Target laps** (1 through 1,000,000; initial default 20).
4. Select two to the configured active lane count of racers, at most eight.
   Their displayed starting lanes remain fixed throughout the race.
5. Confirm setup, position cars just before the timing sensors, and press Space
   for the normal countdown. The first crossing in each lane establishes its
   timing baseline and does **not** count a lap. Subsequent accepted full laps
   count toward the target.
6. Space makes a track call; press it again to resume through the countdown.
   Stopped time is excluded from race elapsed time and lap timing.

The first accepted crossing that brings a racer to the target wins. All lane
power is commanded off immediately, every racer stops, and subsequent crossings
are ignored. There is no finishing-lap grace period for other racers. Normal
sensor filtering still applies: duplicates, raw-lockout edges and too-fast
crossings cannot win. A detected missing sequence does not invent missing laps;
only accepted crossings count.

The board shows elapsed active race time and the leader's laps remaining, then
the winner. There is no time limit. Heat duration and intermission fields are
hidden and ignored in this format, but their saved values are retained for
switching back to timed racing. Race format and lap target are saved separately.

## Results and Stoppages

- Winner: the first target crossing accepted in controller stream order. If
  two crossings have the same millisecond timestamp, their processing order
  decides the winner; later frames cannot change the frozen outcome.
- Other places: completed laps descending, then earlier last counted crossing
  in active race time. Exact ties share a place (for example 1, 2, 2, 4).
  Racers without counted crossings have no last-crossing time.
- During a track call, normal lap corrections are allowed only below the lap
  target. A correction cannot manufacture a winner. Finished results are frozen.
- Current-race rerun is available while paused, with the usual confirmation
  and reason. It discards the attempt from scoring, retains its audit record,
  and resets elapsed time and counts without changing racers, lanes or target.
  Published final results cannot be rerun through this command.
- Controller loss pauses the race. Recovery retains the target, counts and
  elapsed time, leaves power off, and requires explicit director resumption.
  A saved completed event retains the winner and supports normal export retry
  and archive operations. Unknown outage crossings still need director review.

HTML reports include the target, winning elapsed time, finish order and lap
details. Optional JSON and CSV exports retain the outcome and tie-aware places.
JSON export schema 4 adds `lapTarget`, `lapFinish` and `lapFinishStandings`;
see [data exports](RACE_DATA_EXPORT.md). Archive filenames begin `LapFinish_`.

## Scope and Future Expansion

Version one is one race on fixed lanes: no qualifying, qualifying credit,
rotation, multiple groups or individual racer finish periods. Existing timed
heat races and Combined Distance remain separate formats. The explicit format,
target, outcome and standings fields allow later additions without treating a
lap target as a duration or changing the existing formats' rules.

No controller firmware changes are required. Automated scoring/recovery/export
tests and the real-app TCP simulator cover the software path; this does not
replace physical sensor, relay or venue acceptance testing.
