# Combined Distance Racing

Implemented in the development source October 5, 2026. This is a first version
for evaluation, not part of the previously published beta. The scoring rules
are current working decisions and may change with venue-owner input.

## Event setup

1. Select `Mode > Heat Race...` and choose `Combined Distance` in the format
   dropdown. Select racers, segment length, intermission, and race name as usual.
2. The qualifying setup opens next. Choose a lane and duration; this format
   defaults to 60 seconds. Cancelling this dialog leaves the event waiting for
   `Mode > Qualifying...`; racing cannot start without approved qualifying.
3. Use Space for the countdown, track calls, and resume, as in the existing
   format. Qualifying uses active time, excluding track calls. Communication
   loss uses the existing rerun policy for an interrupted qualifier.
4. After each qualifier, confirm its finishing fraction. Once qualifying is
   complete, resolve any exact ties and choose starting lanes for every group.

Existing `Heat Race` scoring and its rotating queue remain unchanged. Independent
starting-lane choices for separate fastest-lap race groups remain a separate TODO.

## Official distance

Distances are expressed in laps, to two decimal places. The model stores integer
hundredths, rather than accumulating floating-point scores.

An approved qualifying result of 10.70 seeds that racer's displayed race total.
Each accepted race lap adds 1.00. The final approved fraction is added once:

`10.70 qualifying + 120 race laps + 0.35 final fraction = 131.05 combined`

Qualifying credit is not counted as laps driven in the first race segment.
The board's cumulative total includes the credit immediately; its heat-lap
counter continues to show laps actually counted in that segment. Reports retain
qualifying, racing, and combined distance separately.

Everyone starts qualifying and their group's first race segment at the starting
line. Timing is seeded when the countdown starts racing, so the first full lap
counts. An immediate departure pulse below the configured minimum lap time is
ignored. Bench-check that minimum and sensor placement on the actual track.
The ordinary heat format's first-crossing behavior is unchanged.

## Groups and rotation

Qualifying ranks form consecutive groups: 1-8, 9-16, and so on on an eight-lane
track. Smaller tracks use their active lane count. Groups are not director-editable
or balanced: ten racers form eight and two, not five and five.

Within each group, highest approved qualifying distance chooses a starting lane
first; subsequent racers choose from remaining lanes. Every lane becomes
available again for the next group's choices. A short group may choose any
active lanes; unused lanes remain unpowered.

Each group runs one timed segment per active lane, following the existing lane
rotation order. Every racer drives every active lane once; racers from the next
group do not enter this group's rotation. The heat counter counts segments
across all groups; the status area identifies the current group.

At each lane change, move the car to the equivalent position in its new lane.
There is no partial-distance credit or adjustment between segments. Its first
crossing after a lane change counts progress but is excluded from fastest-lap
scoring. Timed intermissions work within a group. A new group never starts
automatically: after finishing confirmations, place its cars at the starting
line and press Space when ready.

## Director confirmation

Before the event, prepare a chart mapping numbered track sections to hundredths
of a lap. Section numbers are not necessarily percentages. Use the chart to
translate each observed finishing position into a value from 0 through 99.

The confirmation dialog presents completed laps, an estimated fraction, an
editable fraction in hundredths, and a reason. Explicitly check `Track position
confirmed` before accepting, including when the fraction is zero. Estimated and
approved values, completed laps, confirmation time, and reason are retained.

Confirm fractions only after each qualifying run and after each group's final
race segment. Measuring at the end of that group's race lets its cars be removed
before the next group uses the track. Reports and podium announcements wait
until every racer is confirmed. Lane resets are disabled for this format;
whole-lap corrections remain available during paused racing or intermediate
stopped segments, and use the existing adjustment audit. Final-group fractions
are confirmed against frozen completed-lap totals.

The fraction estimate uses up to five recent positive valid lap times and active
time since the last counted crossing, rounds down, and is capped at 0.99. With
insufficient valid data it is unknown, not an official zero. Final estimates use
the last segment's lap data. A sensor cannot measure car position between
crossings: director confirmation is always required.

## Ties

Qualifying: distance descending, then best valid qualifying lap ascending.
Final results: combined distance descending, then best eligible race lap
ascending. A valid lap wins this tie-break over no valid lap.

If both scores and eligible best laps are equal, the director explicitly confirms
an order in the tie dialog. Move commands only reorder racers within an exact
tie; they cannot override unequal scores. Confirming the displayed order also
counts as a director decision. Orders and decision timestamps are retained in
JSON; final orders appear in the distance report and CSV.

## Live Standings

A separate window opens after qualifying and lane selection. It shows position,
racer, group, lane, qualifying credit, racing distance, combined distance, and
`Projected Final Distance`. Close it when unnecessary; reopen it through
`Mode > Live Standings`.

Current positions use credited distance, not predictions. During racing the
finishing fraction remains uncredited; final confirmed fractions appear in the
totals once approved. Racers are identified by an event-local ID derived from
their original qualifying order. Display names are not export identity keys.

Projection uses counted race laps divided by scheduled active time already
driven, multiplied by the racer's full driving allocation, then adds qualifying
credit. Slow laps and unscheduled stops reduce projected pace. Track-call time,
intermissions, and time when another group races are excluded. Future segments
in that racer's group are included. The projection is unknown until a valid race
lap and positive active time exist, freezes during pauses/intermissions, and
becomes the official combined distance after final confirmation. It is an
estimate, not a measurement or official score.

Before exact ties are resolved, live rows use original racer order as a stable
display fallback; that fallback does not silently finalize tied results.

## Reports and exports

The HTML report contains official combined standings, race lap breakdown,
qualifying history, lane best laps, ordinary lap adjustments, and distance
confirmation records. Its title identifies Combined Distance.

Existing JSON/CSV configuration still applies. JSON archive schema 2 adds a
named race format, distance standings, qualifying approvals and tie decisions,
final approvals, and final tie-order timestamp. Schema 2 is distinct from the
database schema and does not require a database migration.

When CSV is enabled, the existing results/laps/qualifying/adjustments files remain
available, plus:

- `CombinedDistance_<timestamp>_distance.csv`: final position, event racer ID,
  group, qualifying hundredths, counted race laps, final fraction hundredths,
  combined hundredths, eligible best lap, and director tie order.
- `CombinedDistance_<timestamp>_distance_confirmations.csv`: qualifying/race
  confirmation estimates, approved values, timestamps, and reasons.

The existing results/laps CSV integer lap fields describe racing only. Use the
distance CSV or JSON distance standings for official combined scores. Divide
hundredths fields by 100 to express laps; do not add qualifying credit again.
No projection changes an official score. HTML is produced even with both data
export options disabled.

## Validation and remaining work

Automated tests cover precision, estimates, start-line counting, independent
lane choices, 8+2 and 8+8+1 groups, smaller tracks, complete lane visits, track
calls, confirmation gates, exact ties, and HTML/JSON/CSV output. Isolated Windows
dialog checks exercise confirmation and group-choice controls without connecting
to hardware. No controller firmware changes are required.

Bench-test the whole workflow under Windows and Wine, including sensor pulses
at the starting line, equivalent-position lane changes, power masks for short
groups, and interrupted qualifiers. [Active event recovery](ACTIVE_RACE_RECOVERY.md)
now preserves unfinished events and pending fraction/tie decisions, with power
off on restore. Database backup is not an active-race checkpoint. Do not rely
on this first version at a venue until scoring and hardware behavior are verified.
