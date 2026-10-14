# Race Archive Browser and Retention

Use **Data > Race Archives...** while stopped with track power off. The browser
reads archived event snapshots from `ActiveRace.db`, not the settings database
and not a scan of report folders. Existing HTML/JSON files are not automatically
imported. Archives appear when an event is replaced/discarded, or when a
successfully exported completed event is archived on the next app launch.

## Safety Boundaries

The browser never restores an archived event into live racing. It is unavailable
during racing, qualifying runs, countdowns, diagnostics, firmware updates or
pending director dialogs. Opening it pauses any automatic intermission start.
Space/start callbacks are blocked while it is open. Closing leaves power off;
explicit Space is required to continue. The current event's counts/time and
recovery snapshot are not replaced by archive operations.

## Browse and Inspect

Search race names or archive reasons, then use Previous/Next for 50-event pages,
newest first. The list shows archive date, name, format, completion/export status,
last heat, demo flag and archive reason. An unreadable identity is shown rather
than dropping the entire list; preserve such records for diagnosis.

Details shows event identity, saved/archived times, format/scheduling, heat and
qualifying state, lane totals at save, qualifying results and rerun-audit count.
Lane totals at save are not a final-results ranking. Unfinished/discarded events
are inspectable but cannot be exported as final race results.

## Reports and Export Copies

Open Original Report uses the event's saved export location and normal report
viewer/browser behavior. If it is missing/unavailable, use Export Copy.

Only fully completed races can regenerate final reports. Combined Distance must
also have all final approvals and exact-tie decisions; the normal report checks
still apply. Reconstruction uses saved scoring, qualifying credit, corrections,
group assignments and discarded-attempt audits, not current app settings.

Export Copy lets you choose a parent folder and JSON/CSV checkboxes. HTML is
always generated. A unique `ArchiveCopy_<event-id>_<copy-id>` folder is created
for every attempt, using restart-safe staged file publication and a receipt.
The original event timestamp is retained where available. Original report files
and the archive payload/export plan are never overwritten. Export copies do not
pose as original live exports: their notes identify regeneration and the saved
event/snapshot. They use the current application's report generator. They do not
announce a podium or change the active race. Interrupted/failed copy output may
remain in its unique folder; retry produces another copy, not startup Finish
Exports recovery. Remove unwanted partial copy folders deliberately.

No export plan was saved for some older or unfinished records; Open Original
Report then reports that limitation. Completed snapshots can still regenerate
copies if their data is supported. Legacy minute-based snapshot durations are
converted on read, using the same conversion as active-event recovery.

## Delete and Retain

Delete Selected removes one saved archive after a default-No confirmation.
Delete Older Archives considers **all** archives before the selected local
calendar date, regardless of the current search. The confirmation shows the
number and sample names/statuses. Unfinished archived events are included;
unreadable identities/dates are preserved rather than deleted automatically.

Deletion is permanent and includes archived lap history, qualifying decisions
and audits. Keep an appropriate copy first. Settings backups do not include
ActiveRace.db; do not copy a live SQLite database without its consistency needs.
No automatic age-based deletion is enabled. Exported files are never deleted,
and active checkpoint/journal records remain untouched. Transactions and saved
identities protect against stale selections and partially applied deletion batches.

SQLite can reuse freed pages; deleting archives does not promise immediate
filesystem shrinkage. No VACUUM or database replacement occurs during browsing.
Corrupt/future snapshots are refused rather than rewritten as current results.

## Verification

Automated tests cover listing/search/pagination, incomplete/corrupt records,
legacy durations, ordinary/grouped/Combined Distance reconstruction, corrections,
rerun audits, option-controlled fresh export copies, stale/batch deletion rollback,
and preservation of active state and report files. Windows browser layout is
checked in isolation. Full UI/director workflows and ARM64 Wine archive behavior
still require acceptance checks; earlier Wine regression results predate this feature.
