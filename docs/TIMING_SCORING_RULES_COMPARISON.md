# Timing and scoring rules comparison

Reviewed October 5, 2026 against the current development source, including the
first Combined Distance implementation. This is a reference comparison, not a
sanctioning-body approval or a complete compliance audit. No scoring behavior
was changed as a result of this review. Local working decisions remain subject
to venue-owner input.

## Sources and limitations

- [ISRA 2026 rulebook](https://isra-slot.com/wp-content/uploads/2026/03/Rulebook-2026.docx.pdf),
  linked from the [official rulebook page](https://isra-slot.com/documentation/rulebook/).
- [USRA 2018 rulebook](https://www.usraslots.com/rules/2018_USRA_RULES_Final_v1.01.pdf),
  still linked from the [official USRA rules page](https://www.usraslots.com/usra-rules/).
  A newer accessible edition was not verified. USRA comparisons below are
  historical, not statements about current national requirements. A separate
  Google Drive scale-rules link on the BSCRA site could not be accessed.

The comparison covers software timing, scoring, and event workflows. It does
not assess car legality, track construction, electrical installations, or all
administrative requirements. Displaying milliseconds does not prove physical
timing accuracy; controller and sensor accuracy still require measurement.

## ISRA comparison

References are to the 2026 rulebook, principally sections 1.13-1.15, 2.3,
3.2, and 3.3. The rule-side summaries below are deliberately concise.

| Topic | ISRA reference requirement | Current YATSS difference or agreement |
| --- | --- | --- |
| Closest distance format | Production 1/24 is a two-person team race. | Combined Distance tracks individual racers, not team members or equal driving allocations. It is not an implementation of all ISRA classes. |
| Distance qualifying | Two 30-second turns; 15-second changeover. | One qualifying allocation, default 60 seconds; no scheduled driver changeover. |
| Groups | Balance groups; strongest runs last. | Consecutive full groups, then remainder: ten racers form 8+2. Strongest group runs first. |
| Initial lanes | Assign by rank, best on red. | Each group chooses lanes in qualifying order. This is an intentional local rule, not the Production starting-lane rule. |
| Production duration | Eight 7.5-minute segments. | Eight segments on eight lanes, but setup and controller configuration accept whole minutes only. 7.5 minutes cannot be selected. |
| Combined formula | Qualifying plus racing plus final sections. | Agrees conceptually. Qualifying credit remains separate from actual first-segment lap count in reports. |
| Production final ties | Racing distance decides. | Equal combined totals use best eligible race lap, then director-confirmed order. This can reverse the rulebook result. |
| Sprint qualifying ties | Second-best lap decides. | Normal fastest-lap qualifying falls back to original order after equal best laps. Combined qualifying instead uses best lap after equal distance, then director order. |
| Sprint progression | Heats, semifinals, final; previous-stage tie-breaks. | No automatic championship advancement or corresponding multistage tie resolution. Normal heat races use a rotation queue above lane capacity. |
| Counter failure | Rerun the affected segment. | Communication faults pause/recover and permit manual corrections; no dedicated race-segment rollback/rerun workflow. |
| Qualifying track fault | Optional rerun forfeits original attempt. | Ordinary qualifying track calls pause/resume. Communication-loss interruption requires a rerun; no equivalent optional track-fault rerun workflow. |
| Penalties | Defined penalties and disqualification. | Generic audited lap adjustments, not a dedicated penalty or disqualification workflow. |
| Track sections | 100 equal sections. | Hundredths are supported; arbitrary track markings need a director-prepared conversion chart. Software cannot enforce physical section layout. |
| Rotation and restart | Staggered rotation; track calls and countdown. | Eight-lane rotation agrees; qualifying track calls and restart countdown are supported. |

Normal heat-race final results lack fractional-distance scoring and sort equal
whole-lap totals alphabetically. This is a software limitation, not an official
tie-break. Combined Distance requires director-confirmed finishing fractions.

ISRA sprint initial lanes and later-stage lane choices are distinct procedures;
our group-local choice request should not be described as the universal ISRA
starting-lane procedure. Sprint warm-up and full stage scheduling are also not
implemented as an integrated championship workflow.

## USRA historical comparison

References are to the 2018 book's general qualifying, lane rotation, race
procedures, miscellaneous procedures, and Scale Race Procedures sections.
Class-specific exceptions matter; these are not universal rules for every class.

| Topic | USRA 2018 reference | Current YATSS |
| --- | --- | --- |
| Qualifying ties | Backup lap times resolve ties. | Not used by normal qualifying ranking. |
| Scale format | Separate mains seeded by qualifying. | Normal format uses a rotating queue when racers exceed lane count. Combined Distance has separate groups, but different scoring. |
| Eurosport | Balanced preliminary groups and advancement to a final. | Neither championship workflow is implemented. |
| Wing qualifying | Some classes have bye/retry provisions. | No dedicated bye/retry machinery or class-specific qualifying policy. |
| Lane rotation | European eight-lane rotation. | Agrees for eight-lane full rotations; four-lane consolation formats are not a separate event mode. |
| Counter corrections | Corrections and restart options. | Audited whole-lap corrections exist; dedicated segment rollback/restart is absent. |
| Penalties | Prescribed lap penalties/disqualification. | Generic adjustments do not constitute an integrated penalty system. |
| Lane changes | Retain stopping position. | Combined Distance explicitly instructs equivalent-position transfers without intermediate fraction credit. Physical compliance is the director's responsibility. |

Do not assume our combined qualifying carry-forward applies to USRA races.
Duration, qualifying order, lane-choice, and advancement provisions vary by
class; there is no single USRA preset in YATSS.

## Priorities and unresolved questions

1. Decide with the venue owner whether Combined Distance should use racing
   distance as its first final tie-break. Preserve the existing local policy
   until that decision is made; this review does not authorize a behavior change.
2. Revisit group balancing and weakest-to-strongest run order. These differ from
   the currently agreed 8+2, strongest-first implementation.
3. Consider fractional-minute or second-based segment durations, especially
   450-second Production segments, with matching reports, persistence, and tests.
4. Decide whether assigned initial lanes should be another policy alongside
   group-local choices. Keep Production and sprint-stage policies distinct.
5. Consider backup-lap qualifying tie-breaks, dedicated segment reruns, and
   penalties/disqualification before claiming support for sanctioned formats.
6. Obtain current USRA rules and venue-specific amendments before treating the
   historical comparison as an implementation specification.

Keep the name **Combined Distance Racing** and describe the actual local rules.
Do not advertise blanket ISRA/USRA compliance. Estimates and projections remain
unofficial until the director confirms physical finishing position.

The sources inspected did not explicitly settle our post-time coasting-crossing
policy or authorize an average-lap estimate as an official position measurement.
Those require clarification, not an inferred rulebook endorsement. Physical
timing accuracy, start-line sensor behavior, and power-cut timing also remain
bench-validation items.

See [Combined Distance Racing](COMBINED_DISTANCE.md) for current operation and
[the TODO list](../TODO.md) for follow-up. Active-event crash recovery remains a
separate project-wide limitation, not a newly established rulebook requirement.

Implementation update October 9, 2026: [Active event recovery](ACTIVE_RACE_RECOVERY.md)
is now implemented, pending physical/Wine validation. The rulebook comparison
above remains the October 5 assessment; this does not establish a rulebook requirement.
