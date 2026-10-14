# Active event recovery

Heat duration is saved in whole seconds, including non-minute durations such
as 30 or 90 seconds. Journal schema 2 automatically reads schema 1's minute-based
duration as seconds, preserving elapsed time and lap history. Recovery never
interprets an old three-minute heat as three seconds.

Implemented October 9, 2026. This is separate from settings-database backups
and from reconnecting a controller while YATSS remains running.

## Director workflow

An unfinished heat race or qualifying session is saved automatically. After
closing YATSS, a crash, or a restart, the next launch offers:

- **Resume Event**: restore the event with track power off.
- **Archive and Discard**: retain an internal archive and return to practice.
- **Close YATSS**: leave the saved event untouched for another launch.

For a completed race with a saved pending export plan, **Finish Exports**
replaces Resume Event. It retries the report and enabled data files with power
off, without a new countdown or demo-lap stream.

Review totals and actual car positions before pressing Space. A recovered
running heat is paused with its saved remaining active time. Reconnect and
controller verification must also succeed before physical racing can resume.
Recovered intermissions do not automatically start the next heat.

An interrupted qualifier is rerun from the beginning; earlier completed
qualifiers remain. A completed Combined Distance qualifier awaiting fraction
approval resumes that approval rather than rerunning. Pending final fractions,
lane choices, and tie decisions remain part of the event.

The outage is not racing time. Unknown crossings after the last committed save
cannot be reconstructed. The director must reconcile totals and car positions.
After a resumed heat, a crossing with an old timing baseline can count once
without a lap time; it cannot earn a fast-lap award.

## Saved state

Race settings, racers, current/waiting lanes, groups, rotations, lap histories,
carried totals, corrections, remaining active heat time, qualifying results,
approved distances, director tie orders, final-report/export completion, and
the export plan (event ID, timestamp, directory, JSON/CSV choices) are saved.
Demo events are supported; their generator restarts without starting the
race. Ordinary practice is not journaled.

Crossings and director decisions commit before visible acknowledgment. Active
time is checkpointed once per second using the last confirmed controller clock
(the demo clock for demos). Recovery clears live timing/sequence baselines;
saved controller timestamps are history, not a usable new clock baseline.

## Storage and failures

`ActiveRace.db` sits beside the settings database in the user's YATSS local
application-data directory. Under Wine this is inside the selected Wine prefix's
Windows user profile. It is separate SQLite storage using transactions, full
synchronization, WAL, incremental lap histories, and periodic checkpoints.
A lock prevents concurrent YATSS instances owning the same journal.

Physical reception and scoring use separate workers and a bounded queue.
Write failure stops racing, cancels automatic starts, requests power off, and
requires storage repair and restart. Receive overflow also requests power off
and interrupts the connection; lost crossings require review. Software cannot
guarantee power removal with unavailable controller/relay hardware. Existing
watchdog protection and hardware fail-safe checks remain essential.

Unavailable or incompatible recovery storage disables racing rather than
replacing the event. Preserve the database and accompanying `-wal`/`-shm` files;
do not delete them to dismiss an error. Copying a live database alone is not a
reliable backup. Settings backup/restore does not include this journal.

Successfully exported completed events are archived on next launch. Resetting
or abandoning an event archives it before replacement. Data > Race Archives
now provides browsing, completed-report export copies and confirmed manual
retention deletion; no automatic deletion is enabled. See RACE_ARCHIVES.md.
Interrupted exports retry at the original
event-specific filenames with atomic file replacement and a completion receipt.
Choose Finish Exports after correcting an export failure; a completed event only
finishes its exports, never restarts racing. A crash after file publication but
before the journal completion flag causes a safe retry, not another report copy.
The bundle is not an indivisible multi-file transaction. See
[Race Reports and Data Exports](RACE_DATA_EXPORT.md) for guarantees and limits.
Stopped current heats can be voided and rerun before final exports begin.
Starting totals and full duration return, and the discarded attempt is saved
in a separate audit. See [Current-heat reruns](HEAT_RERUNS.md) for the workflow.

## Verification

Automated tests cover compaction, exclusive ownership, forced process termination,
uncommitted-write rollback, locked storage, incompatible schemas, corrections,
rotations, remaining time, interrupted qualifiers, pending approvals, and export
markers. An isolated Windows dialog check verifies startup layout.

Before venue use, perform [Publish smoke test](PUBLISH_SMOKE_TEST.md) checks on
Windows and ARM64 Wine with real sensors and relay wiring. Test OS restart,
physical power loss, and slow/full storage as well as app termination.
No controller sketch changes are required.
