# YATSS Quick Start

YATSS is independent, open-source slot-car timing/scoring software. A demo is
not a physical timing test. Complete the commissioning checklist before venue use.

## Try a demo without hardware

1. Run YATSSWin.exe from the complete published folder.
2. Choose Mode > Demo Race. It supplies demo racers and enables Simulated Lap Input.
3. Press Space to begin the countdown. Space during racing makes a track call;
   press it again to resume through the countdown.
4. During a timed intermission, Space pauses automatic start. The next Space
   starts the next heat. The demo uses its own short heat/intermission settings.
5. Let the race finish. Inspect its report and optional JSON/CSV exports. Under
   Wine the HTML report opens in the host browser.

For simulated practice, choose Mode > Practice and enable Mode > Simulated Lap
Input. Disable simulated input before checking real sensors. Demo laps do not
exercise sensor capture, controller communication or track-power switching.

## Configure a real event

- File > Configure sets active lanes, minimum lap time, sensor settings, speech
  and export preferences. File > Edit Users manages racer names.
- Mode > Heat Race selects racers, race name, Race format, heat length and
  intermission. Durations are seconds: 450 is 7.5 minutes. New setup defaults
  are 180 seconds per heat and 60 seconds between heats; saved choices remain.
- Heat Race uses ordinary lap totals and its rotating queue. Optional fastest-
  lap qualifying is available through Mode > Qualifying.
- Combined Distance requires distance qualifying, finishing-fraction approval
  after each qualifier, and group-local lane choices. Its first group starts
  at the line with qualifying credit already displayed. Transfer cars to
  equivalent positions at lane changes; approve fractions only at group finish.
- Space starts only when controller readiness allows. A reconnect does not
  automatically restore track power. Review totals and car positions first.

## Where to find help

Help opens packaged guides offline, including race operation, Combined Distance,
controller pin maps, wiring, commissioning, troubleshooting, reruns and exports.
Guides display as selectable plain text; Markdown notation may remain visible.
If a guide is missing, YATSS offers its online copy. Report a Bug requires
internet access. Copy Version Information supplies build/platform/controller
details without racer names, database paths or connection addresses.

Data > Open Database Folder and Open Backup Folder locate settings/backup data.
File > Serial Log shows communication diagnostics. Keep active-event recovery
files separate from settings backups. Read the recovery guide before discarding
an unfinished event.
