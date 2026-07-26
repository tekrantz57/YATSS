# Database Backup And Restore

YATSS stores local configuration and racer names in:

```text
%LOCALAPPDATA%\YATSS\laps.db
```

Race reports and JSON/CSV race archives are separate files under Documents and
are not contained in this database. Backing up `laps.db` preserves settings,
lane configuration, controller configuration, racer names, and saved setup
values.

## Data Menu

The Windows app provides these commands under `Data`:

- `Back Up Database...` creates a manually named SQLite backup.
- `Restore Database...` validates and restores a selected YATSS database.
- `Open Database Folder` opens the active database location.
- `Open Backup Folder` opens the default backup location.

Manual backups default to:

```text
%USERPROFILE%\Documents\YATSS Backups
```

YATSS uses SQLite's online backup operation rather than copying an open
database file directly. The backup is first written to a temporary file,
checked with SQLite integrity and foreign-key checks, checked for a supported
YATSS schema, and only then moved to the requested destination.

## Automatic Backups

After the main window appears, YATSS creates at most one verified automatic
backup per calendar day in:

```text
%USERPROFILE%\Documents\YATSS Backups\Automatic
```

Automatic files use the name `YATSS-auto-YYYYMMDD.db`. The newest 14 daily
backups are retained. If today's backup already exists, YATSS verifies it
instead of replacing it. A verification or write failure displays a warning but
does not prevent the app from running.

Before a database schema upgrade, YATSS also creates and verifies a timestamped
copy in the Automatic folder. These safety copies use names such as:

```text
YATSS-before-schema-v0-to-v1-YYYYMMDD-HHMMSS.db
```

Schema safety copies are not part of the 14-file daily-backup retention limit.

## Restore Safety

Restore is available only in Practice mode, with no qualifying session,
countdown, or demo lap stream active. After confirmation, YATSS:

1. Cuts track power.
2. Validates the selected backup before changing the active database.
3. Creates a verified `YATSS-before-restore-YYYYMMDD-HHMMSS.db` copy of the
   current database.
4. Replaces the active database and applies any supported schema upgrade.
5. Verifies the restored active database.
6. Restarts so all restored settings are loaded consistently.

If replacement, migration, or verification fails, YATSS automatically restores
the pre-restore safety copy. An error identifies that safety copy's path. A
backup created by a newer unsupported YATSS schema is rejected.

Keep important backups on another physical device or synchronized storage. The
automatic folder protects against local mistakes and database damage, but it is
on the same computer as the active database.
