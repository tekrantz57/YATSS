# Independent Groups for Ordinary Heat Races

This optional scheduling mode retains ordinary whole-lap scoring. It does not
add qualifying distance, finishing fractions or Combined Distance projections.
The existing Rotation Queue remains the default and is still available.

## Setup and Qualifying

1. Choose Mode > Heat Race, keep Race format set to Heat Race, and select
   Independent Groups under Scheduling. Select racers and configure duration
   and intermission as usual. The scheduling choice is saved for future setup.
2. Click OK. Fastest-lap qualifying setup opens automatically. Choose the lane
   and duration. If cancelled, the event waits for Mode > Qualifying; no race
   start/power-on is permitted until qualifying and lane selection are complete.
3. Run each qualifier with Space/countdown and normal track-call controls.
   Existing fastest-lap ranking applies: best valid lap first, then original
   qualifying order for equal best laps. No-valid-lap racers retain that fallback.
4. Racers choose lanes in qualifying order, independently for every group.
   Used lanes become available again for the next group's choices. All groups'
   choices are collected before racing begins, strongest group first.

The setup list shows qualifying run order, not already-established qualifying
rank or final group membership. Randomize affects that run order. Qualifying
results determine actual groups after all runs finish.

## Groups and Race Controls

Groups are consecutive ranks using the active lane count: on eight lanes,
1-8, 9-16, etc. Ten racers run as eight and two, not balanced groups of five.
Group membership/run order cannot be director-edited in this first version.
A short group may choose any active starting lanes; unused lanes stay off.

Each group completes one heat per active lane and every racer visits each
active lane once. No racers enter from another group during this rotation.
The Heat counter is global: ten racers/eight lanes run sixteen heats total.
The main status area identifies the current group instead of a waiting racer.

Timed intermissions apply inside a group. At its end, power remains off and
the next group always waits for explicit Space. Place its cars at the starting
line before starting. Within a group, move cars to equivalent positions when
changing lanes, using the normal heat-race procedure.

Ordinary first-crossing policy is preserved separately for each group: the
first crossing establishes timing in its first heat. Following a lane change,
the first crossing counts progress but is not eligible for fastest-lap scoring.
This differs from Combined Distance's seeded starting-line first-full-lap rule.
Bench-test sensor position/departure pulses and counting before venue use.

## Scoring, Recovery and Reports

Every group starts with zero racing laps. Qualifying determines rank/lane
choice only; it adds nothing to race totals. Final standings compare latest
cumulative racing laps across all groups, including downward corrections.
Existing ordinary-race equal-total alphabetical ordering remains; this is not
a new sanctioned tie-break or championship advancement policy.

Current-heat reruns retain group assignments and prior groups' results.
Active-event recovery saves scheduling, groups, lane assignments, remaining
time and audit records. An interrupted group change restores with power off
and no automatic start. Qualifying interruption uses the existing rerun policy.

HTML identifies scheduling and lists starting groups/lanes. JSON adds
`race.scheduling` and `race.groups` (group arrays in starting-lane order).
Results CSV adds `Scheduling` and `Group`; Heat remains the global segment
number. Ordinary results remain whole laps. Existing export configuration and
restart-safe publication apply; no special distance files are created.

Combined Distance always uses Independent Groups and disables the scheduling
selector. Switching formats retains the ordinary heat race's chosen scheduling.
For its different scoring/approvals, read COMBINED_DISTANCE.md.

## Verification

Automated checks cover group sizes 2, 8+2, 8+8+1 and smaller tracks, independent
lane choices, all lane visits, empty-lane masks, corrections, reruns, recovery,
settings upgrade/persistence and report exports. Manually validate both Windows
and Wine controls, physical transitions and qualifying before competition.
No controller firmware changes are required.
