# Serial diagnostic logging

Logs are stored at `%LOCALAPPDATA%\YATSS\logs\serial-YYYYMMDD.log`.
Under Wine this is inside that user's Wine prefix, in the Windows local-app-data
folder. Each entry uses the host's local date and time. A running session starts
writing to the new day's file at midnight; restarting YATSS is not required.

## Timing and failures

The timing path queues entries without waiting for disk or debug-listener
writes. One background worker writes batches of up to 64 entries. The queue
holds at most 1,024 entries; individual messages longer than 8,192 characters
are truncated and marked. If the worker is stalled and the queue is full, new
diagnostic entries are dropped rather than blocking accepted laps.

Disk-full, permissions, and directory-creation errors produce a main status-line
warning. The logger does not try to log that warning through the failed file.
It drops diagnostic entries during a 30-second retry interval, then retries
when another entry arrives. A successful write clears the storage-error state.
The `File > Serial Log` window shows the current error and the cumulative count
of dropped entries for this app session, including drops before a successful
recovery. Trace-listener or warning-display exceptions cannot stop the writer.

The log window follows the current day's file, retains the scroll-to-pause
behavior, and switches files on date change even if their sizes match. A read
error shows an unavailable indication and retries on the next refresh without
stopping the application.

## Retention and shutdown

On its first successful write each local day, the worker attempts to remove
dated `serial-YYYYMMDD.log` files older than the last 30 calendar days, including
today. It leaves unrelated and unrecognized filenames alone. Cleanup failure
is reported without blocking further writes or racing. Copy logs elsewhere
before this retention window expires if they are needed for investigation.

On orderly exit, the app closes the queue and allows up to two seconds for the
worker to drain it. A stalled disk or listener cannot hold shutdown indefinitely.
Forced termination, power loss, queue overload, or a storage failure can lose
diagnostic entries. These logs are not a durable race journal or a substitute
for completed race reports and exports. Active-race crash recovery remains a
separate TODO item. Retention limits the number of calendar days, not the size
of an individual day's file.

## Verification

Automated tests cover simulated disk-full and permission failures, a real
directory-creation failure, retry/recovery, bounded queue overflow with a
stalled writer, throwing listeners and warning callbacks, entry truncation,
midnight rollover, and retention. They also exercise lap counting while logging
is unavailable. Visual log-window checks and sustained Windows/Wine operation
still require a bench test.
