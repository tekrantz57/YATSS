# Current-heat reruns

Implemented October 10, 2026. YATSS is an independent open-source alternative,
not an effort to replace ISRA's established software. This director-controlled
operation is not a claim of blanket ISRA/USRA compliance.

## Director workflow

1. Stop racing with a track call, or pause the intermission after a heat.
2. Select **Mode > Rerun Current Heat...**. Only the current paused/completed
   heat is eligible, with track power off. Opening confirmation cancels the
   intermission timer and prevents Space/automatic starts while the dialog is open.
3. Enter a reason and choose **Discard and Rerun**, or Cancel. Cancel/closing
   leaves the heat unchanged and automatic start paused.
4. Restore cars to their exact positions at the beginning of this heat, not
   their positions at the interruption. If the heat began at the starting line,
   start there again. Lane assignments and the waiting queue do not change.
5. Review restored totals and press Space when ready. Normal countdown and
   controller-readiness checks apply. Rerun never enables power itself.

## Scoring and audit

All scoring from this attempt is removed: accepted laps, current-heat manual
corrections, elapsed time and lap-time statistics. Starting cumulative totals
return and the full configured duration is available. Previous heats, earlier
corrections, qualifying credit, lane rotation, waiting racers and first-crossing
policy are preserved.

Combined Distance clears current racers' final finishing approvals and the
final tie order; earlier groups' approvals remain. Finishing positions must be
confirmed again after the replacement attempt.

Each discarded attempt retains its reason, time, elapsed racing time, lane
results, laps, manual corrections and finishing approvals separately from
official scoring. Repeated attempts are numbered per heat. Only the replacement
attempt contributes to official results.

HTML contains a **Voided Heat Attempts** summary. JSON contains full
`race.reruns` records. With CSV enabled and reruns present, `_reruns.csv` has one
row per discarded attempt and nested JSON columns for discarded lane results,
laps, corrections and finishing approvals. Normal lap/result/correction files
contain only official attempts. Saved export plans and receipts still apply.

## Recovery and limits

Rollback and audit are journaled together before the board displays restored
totals. Restart restores Ready, full duration and power off. Unchanged discarded
histories are omitted from ordinary delta checkpoints. Save failure stops
racing: correct storage and restart to recover the last committed state.

Only the current heat can be rerun; preparing the next heat closes this option
for its predecessor. Practice, qualifying, running heats, countdowns, diagnostics,
firmware updates and active distance-approval dialogs are excluded. Final exports
also close rerun, even if those exports failed and await retry. To rerun a final
heat, pause before completion initiates final reporting. Published reports are
not rewritten.

Software cannot reconstruct physical car positions or unrecorded crossings.
The director must review them and decide whether to rerun. No controller firmware
changes are required. See [Publish smoke test](PUBLISH_SMOKE_TEST.md) for manual
Windows/Wine checks.
