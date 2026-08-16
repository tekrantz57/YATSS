using System.Globalization;
using Microsoft.Data.Sqlite;

namespace YATSS
{
    internal sealed record DatabaseBackupResult(string Path, int RacerCount);

    internal sealed record DatabaseRestoreResult(
        string RestoredFromPath,
        string SafetyBackupPath,
        int RacerCount);

    internal sealed class DatabaseMaintenance
    {
        private readonly string _databasePath;
        private readonly string _automaticBackupDirectory;
        private readonly int _currentSchemaVersion;
        private static readonly DatabaseTableRequirement LegacyUserTable =
            new("users", ["name"]);
        private static readonly DatabaseTableRequirement[] SchemaOneRequirements =
        [
            LegacyUserTable,
            new("comports", ["name"]),
            new("heat_race_settings", ["id", "heat_length_minutes", "between_heats_seconds"]),
            new("app_settings", [
                "id",
                "min_lap_milliseconds",
                "sound_on_too_fast_lap",
                "speech_voice_name",
                "active_lane_count"
            ]),
            new("lane_settings", ["lane_index", "display_name", "color_argb"]),
            new("race_report_settings", ["id", "export_json", "export_csv"]),
            new("heat_race_identity", ["id", "race_name"]),
            new("track_configuration", ["id", "track_length_feet"]),
            new("qualifying_settings", ["id", "lane_index", "duration_seconds"]),
            new("controller_settings", ["id", "debounce_milliseconds"])
        ];

        public DatabaseMaintenance(
            string databasePath,
            string automaticBackupDirectory,
            int currentSchemaVersion)
        {
            _databasePath = Path.GetFullPath(databasePath);
            _automaticBackupDirectory = Path.GetFullPath(automaticBackupDirectory);
            _currentSchemaVersion = currentSchemaVersion;
        }

        public DatabaseBackupResult? CreateAutomaticBackup(int retainedBackupCount = 14)
        {
            if (retainedBackupCount < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(retainedBackupCount),
                    "At least one automatic backup must be retained.");
            }

            Directory.CreateDirectory(_automaticBackupDirectory);
            string backupPath = Path.Combine(
                _automaticBackupDirectory,
                $"YATSS-auto-{DateTime.Now:yyyyMMdd}.db");
            if (File.Exists(backupPath))
            {
                _ = InspectDatabase(backupPath, backupPath, requireCurrentSchema: false);
                if (GetSchemaVersion(backupPath) == _currentSchemaVersion)
                {
                    return null;
                }

                backupPath = Path.Combine(
                    _automaticBackupDirectory,
                    $"YATSS-auto-{DateTime.Now:yyyyMMdd}-v{_currentSchemaVersion}.db");
                if (File.Exists(backupPath))
                {
                    _ = InspectDatabase(backupPath, backupPath, requireCurrentSchema: true);
                    return null;
                }
            }

            DatabaseBackupResult result = CreateBackup(backupPath);
            PruneAutomaticBackups(retainedBackupCount);
            return result;
        }

        public DatabaseBackupResult CreateBackup(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
            {
                throw new ArgumentException("A backup path is required.", nameof(backupPath));
            }

            string destinationPath = Path.GetFullPath(backupPath);
            if (PathsEqual(destinationPath, _databasePath))
            {
                throw new ArgumentException(
                    "The backup must be saved separately from the active database.",
                    nameof(backupPath));
            }

            string destinationDirectory = Path.GetDirectoryName(destinationPath)
                ?? throw new ArgumentException("The backup path has no directory.", nameof(backupPath));
            Directory.CreateDirectory(destinationDirectory);
            string temporaryPath = Path.Combine(
                destinationDirectory,
                $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                CopyDatabase(_databasePath, temporaryPath);
                DatabaseBackupResult result = InspectDatabase(
                    temporaryPath,
                    destinationPath,
                    requireCurrentSchema: true);
                File.Move(temporaryPath, destinationPath, overwrite: true);
                return result;
            }
            finally
            {
                DeleteDatabaseFiles(temporaryPath);
            }
        }

        public DatabaseRestoreResult RestoreBackup(
            string backupPath,
            string safetyBackupPath,
            Action closeActiveDatabase,
            Action initializeActiveDatabase)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
            {
                throw new ArgumentException("A backup path is required.", nameof(backupPath));
            }

            string sourcePath = Path.GetFullPath(backupPath);
            if (PathsEqual(sourcePath, _databasePath))
            {
                throw new ArgumentException(
                    "Select a backup rather than the active database.",
                    nameof(backupPath));
            }
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("The selected database backup was not found.", sourcePath);
            }

            string safetyPath = Path.GetFullPath(safetyBackupPath);
            if (PathsEqual(sourcePath, safetyPath))
            {
                throw new ArgumentException(
                    "The safety backup must not overwrite the selected restore file.",
                    nameof(safetyBackupPath));
            }

            _ = InspectDatabase(sourcePath, sourcePath, requireCurrentSchema: false);
            DatabaseBackupResult safetyBackup = CreateBackup(safetyPath);

            try
            {
                closeActiveDatabase();
                ReplaceDatabase(sourcePath, _databasePath);
                initializeActiveDatabase();
                DatabaseBackupResult restored = InspectDatabase(
                    _databasePath,
                    _databasePath,
                    requireCurrentSchema: true);
                return new DatabaseRestoreResult(
                    sourcePath,
                    safetyBackup.Path,
                    restored.RacerCount);
            }
            catch (Exception restoreException)
            {
                try
                {
                    closeActiveDatabase();
                    ReplaceDatabase(safetyBackup.Path, _databasePath);
                    initializeActiveDatabase();
                }
                catch (Exception rollbackException)
                {
                    throw new InvalidOperationException(
                        $"The restore failed, and YATSS could not automatically restore the " +
                        $"previous database. The safety backup is at '{safetyBackup.Path}'.",
                        new AggregateException(restoreException, rollbackException));
                }

                throw new InvalidOperationException(
                    $"The restore failed. The previous database was restored automatically. " +
                    $"The safety backup is at '{safetyBackup.Path}'.",
                    restoreException);
            }
        }

        public void BackUpBeforeSchemaUpgrade()
        {
            if (!File.Exists(_databasePath) || new FileInfo(_databasePath).Length == 0)
            {
                return;
            }

            int version = GetSchemaVersion(_databasePath);
            if (version > _currentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"The database uses newer schema version {version}; this version of YATSS " +
                    $"supports version {_currentSchemaVersion}.");
            }
            if (version == _currentSchemaVersion)
            {
                return;
            }

            Directory.CreateDirectory(_automaticBackupDirectory);
            string backupPath = Path.Combine(
                _automaticBackupDirectory,
                $"YATSS-before-schema-v{version}-to-v{_currentSchemaVersion}-" +
                $"{DateTime.Now:yyyyMMdd-HHmmss}.db");
            try
            {
                CopyDatabase(_databasePath, backupPath);
                VerifyIntegrityOnly(backupPath);
            }
            catch
            {
                DeleteDatabaseFiles(backupPath);
                throw;
            }
        }

        private DatabaseBackupResult InspectDatabase(
            string databasePath,
            string reportedPath,
            bool requireCurrentSchema)
        {
            using SqliteConnection connection = OpenReadOnly(databasePath);

            using (SqliteCommand integrityCommand = connection.CreateCommand())
            {
                integrityCommand.CommandText = "PRAGMA integrity_check;";
                string? result = Convert.ToString(
                    integrityCommand.ExecuteScalar(),
                    CultureInfo.InvariantCulture);
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"SQLite could not verify the backup: {result ?? "unknown error"}");
                }
            }

            int version;
            using (SqliteCommand versionCommand = connection.CreateCommand())
            {
                versionCommand.CommandText = "PRAGMA user_version;";
                version = Convert.ToInt32(
                    versionCommand.ExecuteScalar(),
                    CultureInfo.InvariantCulture);
                if (version > _currentSchemaVersion ||
                    (requireCurrentSchema && version != _currentSchemaVersion))
                {
                    throw new InvalidDataException(
                        $"This database uses schema version {version}; " +
                        $"YATSS requires version {_currentSchemaVersion}.");
                }
            }

            ValidateRequiredSchema(connection, version);

            using (SqliteCommand foreignKeyCommand = connection.CreateCommand())
            {
                foreignKeyCommand.CommandText = "PRAGMA foreign_key_check;";
                using SqliteDataReader reader = foreignKeyCommand.ExecuteReader();
                if (reader.Read())
                {
                    throw new InvalidDataException(
                        "The database contains invalid relationships and cannot be restored.");
                }
            }

            using SqliteCommand countCommand = connection.CreateCommand();
            countCommand.CommandText = "SELECT COUNT(*) FROM users;";
            int racerCount;
            try
            {
                racerCount = Convert.ToInt32(
                    countCommand.ExecuteScalar(),
                    CultureInfo.InvariantCulture);
            }
            catch (SqliteException exception)
            {
                throw new InvalidDataException(
                    "The selected file is not a readable YATSS database.",
                    exception);
            }

            return new DatabaseBackupResult(reportedPath, racerCount);
        }

        private static void ValidateRequiredSchema(SqliteConnection connection, int schemaVersion)
        {
            foreach (DatabaseTableRequirement requirement in GetRequiredSchema(schemaVersion))
            {
                HashSet<string>? columns = GetTableColumns(connection, requirement.TableName);
                if (columns is null)
                {
                    throw new InvalidDataException(
                        $"The database is missing required table '{requirement.TableName}'.");
                }

                string[] missingColumns = requirement.Columns
                    .Where(column => !columns.Contains(column))
                    .ToArray();
                if (missingColumns.Length > 0)
                {
                    throw new InvalidDataException(
                        $"The database table '{requirement.TableName}' is missing required " +
                        $"column{(missingColumns.Length == 1 ? "" : "s")} " +
                        $"{string.Join(", ", missingColumns.Select(column => $"'{column}'"))}.");
                }
            }
        }

        private static IReadOnlyList<DatabaseTableRequirement> GetRequiredSchema(int schemaVersion)
        {
            if (schemaVersion <= 0)
            {
                return [LegacyUserTable];
            }

            List<DatabaseTableRequirement> requirements = SchemaOneRequirements
                .Select(requirement => requirement with { Columns = requirement.Columns.ToArray() })
                .ToList();
            if (schemaVersion >= 2)
            {
                AddRequiredColumn(requirements, "app_settings", "voice_announcements_enabled");
            }
            if (schemaVersion >= 3)
            {
                AddRequiredColumn(requirements, "app_settings", "speech_backend");
                AddRequiredColumn(requirements, "controller_settings", "raw_sensor_lockout_milliseconds");
            }
            if (schemaVersion >= 4)
            {
                AddRequiredColumn(requirements, "app_settings", "lap_best_sounds_enabled");
            }
            return requirements;
        }

        private static void AddRequiredColumn(
            List<DatabaseTableRequirement> requirements,
            string tableName,
            string columnName)
        {
            int index = requirements.FindIndex(requirement =>
                string.Equals(requirement.TableName, tableName, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || requirements[index].Columns.Contains(columnName, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            requirements[index] = requirements[index] with
            {
                Columns = [.. requirements[index].Columns, columnName]
            };
        }

        private static HashSet<string>? GetTableColumns(SqliteConnection connection, string tableName)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)});";
            using SqliteDataReader reader = command.ExecuteReader();
            HashSet<string> columns = new(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }
            return columns.Count == 0 ? null : columns;
        }

        private static string QuoteIdentifier(string identifier) =>
            "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

        private static void VerifyIntegrityOnly(string databasePath)
        {
            using SqliteConnection connection = OpenReadOnly(databasePath);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            string? result = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"SQLite could not verify the safety backup: {result ?? "unknown error"}");
            }
        }

        private static int GetSchemaVersion(string databasePath)
        {
            using SqliteConnection connection = OpenReadOnly(databasePath);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private void PruneAutomaticBackups(int retainedBackupCount)
        {
            IEnumerable<FileInfo> obsoleteBackups = new DirectoryInfo(_automaticBackupDirectory)
                .EnumerateFiles("YATSS-auto-*.db", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Skip(retainedBackupCount);
            foreach (FileInfo obsoleteBackup in obsoleteBackups)
            {
                obsoleteBackup.Delete();
            }
        }

        private static void ReplaceDatabase(string sourcePath, string destinationPath)
        {
            DeleteDatabaseSidecars(destinationPath);
            CopyDatabase(sourcePath, destinationPath);
            DeleteDatabaseSidecars(destinationPath);
        }

        private static void CopyDatabase(string sourcePath, string destinationPath)
        {
            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            using SqliteConnection source = OpenReadOnly(sourcePath);
            using SqliteConnection destination = new(new SqliteConnectionStringBuilder
            {
                DataSource = destinationPath,
                ForeignKeys = true,
                Pooling = false
            }.ToString());
            destination.Open();
            source.BackupDatabase(destination);
        }

        private static SqliteConnection OpenReadOnly(string databasePath)
        {
            SqliteConnection connection = new(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                ForeignKeys = true,
                Pooling = false
            }.ToString());
            connection.Open();
            return connection;
        }

        private static bool PathsEqual(string first, string second)
            => string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);

        private static void DeleteDatabaseFiles(string databasePath)
        {
            File.Delete(databasePath);
            DeleteDatabaseSidecars(databasePath);
        }

        private static void DeleteDatabaseSidecars(string databasePath)
        {
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }

        private sealed record DatabaseTableRequirement(string TableName, string[] Columns);
    }
}
