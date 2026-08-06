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

            using (SqliteCommand versionCommand = connection.CreateCommand())
            {
                versionCommand.CommandText = "PRAGMA user_version;";
                int version = Convert.ToInt32(
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
    }
}
