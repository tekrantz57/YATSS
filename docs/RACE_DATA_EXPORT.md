# Race Reports and Data Exports

YATSS creates race artifacts when the final heat completes. The HTML report is
the primary human-readable result. Optional JSON and CSV exports preserve the
same scoring data for spreadsheets, custom reports, websites, and other tools.

## Configuration

Open `Configure` and use the `Race Reports` options:

- `Write JSON race archive` writes the complete, versioned machine-readable
  archive.
- `Write CSV data files` writes normalized tables for spreadsheet and script
  use.

Both options default to enabled and are persisted in the YATSS SQLite settings
database. They can be enabled independently. The HTML report is always written
and displayed when a race completes.

## Output Location and Names

Artifacts are written under:

```text
%USERPROFILE%\Documents\YATSS Race Reports
```

Every artifact from one race shares a timestamped basename:

```text
HeatRace_yyyyMMdd_HHmmss.html
HeatRace_yyyyMMdd_HHmmss.json
HeatRace_yyyyMMdd_HHmmss_results.csv
HeatRace_yyyyMMdd_HHmmss_laps.csv
HeatRace_yyyyMMdd_HHmmss_qualifying.csv
HeatRace_yyyyMMdd_HHmmss_adjustments.csv
```

Only the HTML file is present when both optional exports are disabled. Disabling
JSON does not affect CSV, and disabling CSV does not affect JSON.

## Scoring Boundaries

The archive records accepted race-scoring information, not every controller
message:

- A lane's first practice or first-heat edge establishes its timing baseline
  and is not exported as a lap.
- Accepted timed laps are exported individually.
- A counted lap without a duration is retained with a null/empty lap time.
- Laps rejected by minimum-time, raw-edge-lockout, sequence, or other validity
  checks are not race laps and remain documented in the serial log.
- Paused time and between-heat time are excluded from race elapsed timing.
- The first crossing after a lane rotation counts from the heat start but is
  marked ineligible for fastest-lap awards because it can represent a partial
  physical lap.
- Manual lap additions and subtractions change official totals but do not
  manufacture timing samples. They are exported as separate audit entries.

The serial log remains the source for raw frames, rejected edges, controller
diagnostics, and communication troubleshooting.

## HTML Report

The HTML report includes race settings, qualifying, finish order, fastest laps
by lane, heat details, and any manual lap corrections. The qualifying table
contains:

- Ranked position and racer
- Qualifying lane
- Configured and actual session duration
- Accepted-lap count
- Best qualifying lap
- Complete accepted qualifying-lap history

YATSS displays this report in an owned report window after completion. The
window can also open the HTML in the default browser or reveal it in File
Explorer.

## JSON Archive

The JSON file is the canonical lossless export of accepted race-scoring data.
Property names use camel case, times are numeric milliseconds unless documented
otherwise, and the file is UTF-8 without a byte-order mark.

### Top Level

| Property | Meaning |
| --- | --- |
| `schemaVersion` | Integer contract version. The initial version is `1`. |
| `applicationVersion` | YATSS assembly version that produced the archive. |
| `exportedAt` | ISO 8601 timestamp with UTC offset for archive creation. |
| `race` | Complete race-report object described below. |

Consumers should reject unsupported higher schema versions or ignore unknown
properties. YATSS will increment `schemaVersion` when an incompatible contract
change is introduced.

### Race Object

| Property | Meaning |
| --- | --- |
| `createdLocal` | Local date and time used for the artifact basename. |
| `raceName` | Configured race name; may be empty. |
| `heatLengthMinutes` | Configured active duration of each heat. |
| `betweenHeatsSeconds` | Configured intermission duration. |
| `trackLengthFeet` | Configured physical track length. |
| `totalHeats` | Number of scheduled heats. |
| `laneNames` | Active physical lane names in zero-based lane order. |
| `laneColorArgb` | Active lane colors as signed .NET ARGB integers. |
| `qualifyingResults` | Ranked qualifying session records. |
| `racers` | Final standings and per-racer aggregate results. |
| `laneResults` | One aggregate result for each occupied lane in each heat. |
| `laps` | Every accepted heat-race lap record. |
| `manualAdjustments` | Ordered manual correction audit entries. |
| `notes` | Human-readable scoring notes. |

JSON `laneIndex` values are zero based. CSV files expose one-based
`LaneNumber` values for human-facing use.

### Qualifying Result

| Property | Meaning |
| --- | --- |
| `racerName` | Racer who ran the session. |
| `originalOrder` | Zero-based order before qualifying. |
| `bestLapMilliseconds` | Fastest accepted lap, or null when none was set. |
| `laneIndex` | Zero-based physical qualifying lane. |
| `configuredDurationSeconds` | Requested session duration. |
| `elapsedMilliseconds` | Actual controller elapsed time at completion. |
| `laps` | Accepted qualifying laps in crossing order. |

Each qualifying lap contains `lapNumber`, `lapMilliseconds`, and
`sessionElapsedMilliseconds`. The elapsed value is measured from the start of
that racer's qualifying session to the recorded crossing.

### Final Racer Result

Each item in `racers` contains `racerName`, `totalLaps`, `heatLaps`, and
`bestLapByLaneMilliseconds`. The array is sorted in final finishing order.
The heat-lap array uses zero-based array position for Heat 1, Heat 2, and so on.
The best-lap array uses zero-based physical lane position.

### Lane Result

Each item in `laneResults` contains `heatNumber`, `laneIndex`, `laneName`,
`racerName`, `heatLaps`, `totalLaps`, and `bestLapMilliseconds`. `totalLaps` is
that racer's cumulative total at the end of the recorded heat.

### Heat Lap

Each item in `laps` contains:

| Property | Meaning |
| --- | --- |
| `heatNumber` | One-based heat number. |
| `laneIndex` / `laneName` | Physical lane identity. |
| `racerName` | Racer assigned to the lane for that heat. |
| `lapNumberInHeat` | One-based accepted crossing number in that heat. |
| `racerTotalLapNumber` | Racer's cumulative lap number at that crossing, before later manual corrections. |
| `lapMilliseconds` | Measured duration, or null for an untimed counted crossing. |
| `raceElapsedMilliseconds` | Active race time at the crossing, excluding pauses and intermissions. |
| `fastestLapEligible` | Whether this timing sample may win a fastest-lap award. |

### Manual Adjustment

Each item in `manualAdjustments` contains the heat, lane, racer, signed `delta`,
`resultingTotalLaps`, active `raceElapsedMilliseconds`, and an ISO 8601
`recordedAt` timestamp. Multiple changes are retained separately, including a
later correction that reverses an earlier one.

## CSV Exports

CSV files use UTF-8 without a byte-order mark, invariant-culture numbers,
lowercase `true`/`false`, comma delimiters, and doubled quotes inside quoted
fields. Empty optional values are blank. A CSV with no applicable records still
contains its header row.

### Results CSV

One row is written for each occupied lane in each heat.

| Column | Meaning |
| --- | --- |
| `RaceName` | Configured race name. |
| `CreatedLocal` | ISO 8601 local race-report time. |
| `FinalPlace` | Racer's one-based final position. |
| `Heat` | One-based heat number. |
| `LaneNumber` / `LaneName` | One-based physical lane identity and configured name. |
| `Racer` | Racer assigned to that lane. |
| `HeatLaps` | Official laps credited during the heat, including manual corrections. |
| `TotalLaps` | Official cumulative total after the heat. |
| `BestLapMilliseconds` | Fastest eligible measured lap in that heat/lane, or blank. |

### Laps CSV

One row is written for every accepted heat-race lap. Columns are `RaceName`,
`Heat`, `LaneNumber`, `LaneName`, `Racer`, `LapNumberInHeat`,
`RacerTotalLapNumber`, `LapMilliseconds`, `RaceElapsedMilliseconds`, and
`FastestLapEligible`.

Manual additions do not appear as fabricated rows in this file. Use the
adjustments CSV with the results CSV to audit how measured laps became official
totals.

### Qualifying CSV

One row is written for every accepted qualifying lap. A qualifier with no valid
lap still receives one row with blank lap fields. Columns are `RaceName`,
`Position`, `OriginalOrder`, `LaneNumber`, `LaneName`, `Racer`,
`ConfiguredDurationSeconds`, `ElapsedMilliseconds`, `LapNumber`,
`LapMilliseconds`, `SessionElapsedMilliseconds`, and `IsBestLap`.

`Position`, `OriginalOrder`, and `LaneNumber` are one based in CSV. When equal
lap times tie for the best time, every matching row has `IsBestLap=true`.

### Adjustments CSV

One row is written for each manual lap correction. Columns are `RaceName`,
`Heat`, `LaneNumber`, `LaneName`, `Racer`, `Delta`, `ResultingTotalLaps`,
`RaceElapsedMilliseconds`, and `RecordedAt`.

`Delta` is signed: positive values add laps and negative values subtract laps.
`RecordedAt` is an ISO 8601 timestamp with UTC offset.

## Schema Lifecycle

YATSS has no deployed installations at the time schema version 1 is introduced,
so this initial contract does not include migration or legacy-report behavior.
The `schemaVersion` field gives future consumers an explicit way to detect
contract changes once exported archives exist in normal use.
