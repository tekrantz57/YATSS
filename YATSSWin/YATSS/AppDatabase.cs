using Microsoft.Data.Sqlite;

namespace YATSS
{
    internal sealed record HeatRaceSetupSettings(
        int HeatLengthMinutes,
        int BetweenHeatsSeconds,
        string RaceName);
    internal sealed record QualifyingSetupSettings(int LaneIndex, int DurationSeconds);
    internal sealed record RaceReportSettings(bool ExportJson, bool ExportCsv);
    internal sealed record AppSettings(
        int MinLapMilliseconds,
        bool SoundOnTooFastLap,
        bool VoiceAnnouncementsEnabled,
        string SpeechVoiceName,
        SpeechBackendMode SpeechBackend,
        int ActiveLaneCount);

    internal static class AppDatabase
    {
        private const int CurrentSchemaVersion = 3;
        public const int DefaultSensorDebounceMilliseconds = 1800;
        public const int DefaultRawSensorLockoutMilliseconds = 0;
        private static readonly object SyncRoot = new();
        private static readonly string DatabaseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YATSS");
        public static string DatabasePath { get; } = DatabaseFileMigration.MoveLegacyDatabase(
            Path.Combine(DatabaseDirectory, "laps.db"),
            Path.Combine(DatabaseDirectory, "YATSS.db"));
        private static readonly string AutomaticBackupDirectory = Path.Combine(
            GetDefaultBackupDirectory(),
            "Automatic");
        private static readonly DatabaseMaintenance Maintenance = new(
            DatabasePath,
            AutomaticBackupDirectory,
            CurrentSchemaVersion);
        private static readonly string ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        public static SqliteConnection Connection { get; } = new(ConnectionString);

        public static void Open()
        {
            lock (SyncRoot)
            {
                if (Connection.State != System.Data.ConnectionState.Open)
                {
                    string? dataSource = new SqliteConnectionStringBuilder(ConnectionString).DataSource;
                    if (!string.IsNullOrWhiteSpace(dataSource))
                    {
                        string? directory = Path.GetDirectoryName(dataSource);
                        if (!string.IsNullOrWhiteSpace(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                    }

                    Maintenance.BackUpBeforeSchemaUpgrade();
                    Connection.Open();
                }

                EnsureSchema();
            }
        }

        public static string GetDefaultBackupDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "YATSS Backups");
        }

        public static DatabaseBackupResult? CreateAutomaticBackup(int retainedBackupCount = 14)
        {
            lock (SyncRoot)
            {
                Open();
                return Maintenance.CreateAutomaticBackup(retainedBackupCount);
            }
        }

        public static DatabaseBackupResult CreateBackup(string backupPath)
        {
            lock (SyncRoot)
            {
                Open();
                return Maintenance.CreateBackup(backupPath);
            }
        }

        public static DatabaseRestoreResult RestoreBackup(string backupPath, string safetyBackupPath)
        {
            lock (SyncRoot)
            {
                Open();
                return Maintenance.RestoreBackup(
                    backupPath,
                    safetyBackupPath,
                    closeActiveDatabase: () => Connection.Close(),
                    initializeActiveDatabase: () =>
                    {
                        Connection.Open();
                        EnsureSchema();
                    });
            }
        }

        public static string LoadSerialPort()
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"SELECT name FROM comports LIMIT 1";
            return command.ExecuteScalar()?.ToString()?.Trim() ?? string.Empty;
        }

        public static void SaveSerialPort(string portName)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"UPDATE comports SET name = $name";
            command.Parameters.AddWithValue("$name", portName.Trim());
            if (command.ExecuteNonQuery() > 0)
            {
                return;
            }

            using SqliteCommand insert = Connection.CreateCommand();
            insert.CommandText = @"INSERT INTO comports (name) VALUES ($name)";
            insert.Parameters.AddWithValue("$name", portName.Trim());
            insert.ExecuteNonQuery();
        }

        public static AppSettings LoadAppSettings(AppSettings defaults)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"
                SELECT min_lap_milliseconds, sound_on_too_fast_lap,
                       voice_announcements_enabled, speech_voice_name,
                       speech_backend, active_lane_count
                FROM app_settings
                WHERE id = 1";

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return defaults;
            }

            SpeechBackendMode speechBackend =
                !reader.IsDBNull(4) && Enum.TryParse(
                    reader.GetString(4),
                    ignoreCase: true,
                    out SpeechBackendMode storedBackend)
                    ? storedBackend
                    : defaults.SpeechBackend;
            return new AppSettings(
                reader.IsDBNull(0) ? defaults.MinLapMilliseconds : reader.GetInt32(0),
                reader.IsDBNull(1) ? defaults.SoundOnTooFastLap : reader.GetBoolean(1),
                reader.IsDBNull(2) ? defaults.VoiceAnnouncementsEnabled : reader.GetBoolean(2),
                reader.IsDBNull(3) ? defaults.SpeechVoiceName : reader.GetString(3),
                speechBackend,
                reader.IsDBNull(5) ? defaults.ActiveLaneCount : reader.GetInt32(5));
        }

        public static void SaveAppSettings(AppSettings settings)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO app_settings (
                    id, min_lap_milliseconds, sound_on_too_fast_lap,
                    voice_announcements_enabled, speech_voice_name,
                    speech_backend, active_lane_count)
                VALUES (1, $minLapMilliseconds, $soundOnTooFastLap,
                    $voiceAnnouncementsEnabled, $speechVoiceName,
                    $speechBackend, $activeLaneCount)
                ON CONFLICT(id) DO UPDATE SET
                    min_lap_milliseconds = excluded.min_lap_milliseconds,
                    sound_on_too_fast_lap = excluded.sound_on_too_fast_lap,
                    voice_announcements_enabled = excluded.voice_announcements_enabled,
                    speech_voice_name = excluded.speech_voice_name,
                    speech_backend = excluded.speech_backend,
                    active_lane_count = excluded.active_lane_count";
            command.Parameters.AddWithValue("$minLapMilliseconds", settings.MinLapMilliseconds);
            command.Parameters.AddWithValue("$soundOnTooFastLap", settings.SoundOnTooFastLap);
            command.Parameters.AddWithValue("$voiceAnnouncementsEnabled", settings.VoiceAnnouncementsEnabled);
            command.Parameters.AddWithValue("$speechVoiceName", settings.SpeechVoiceName.Trim());
            command.Parameters.AddWithValue("$speechBackend", settings.SpeechBackend.ToString());
            command.Parameters.AddWithValue("$activeLaneCount", settings.ActiveLaneCount);
            command.ExecuteNonQuery();
        }

        public static RaceReportSettings LoadRaceReportSettings(RaceReportSettings defaults)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"
                SELECT export_json, export_csv
                FROM race_report_settings
                WHERE id = 1";
            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Read()
                ? new RaceReportSettings(reader.GetBoolean(0), reader.GetBoolean(1))
                : defaults;
        }

        public static void SaveRaceReportSettings(RaceReportSettings settings)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO race_report_settings (id, export_json, export_csv)
                VALUES (1, $exportJson, $exportCsv)
                ON CONFLICT(id) DO UPDATE SET
                    export_json = excluded.export_json,
                    export_csv = excluded.export_csv";
            command.Parameters.AddWithValue("$exportJson", settings.ExportJson);
            command.Parameters.AddWithValue("$exportCsv", settings.ExportCsv);
            command.ExecuteNonQuery();
        }

        public static double LoadTrackLengthFeet(double defaultValue)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"SELECT track_length_feet FROM track_configuration WHERE id = 1";
            object? value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? defaultValue
                : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static void SaveTrackLengthFeet(double trackLengthFeet)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO track_configuration (id, track_length_feet)
                VALUES (1, $trackLengthFeet)
                ON CONFLICT(id) DO UPDATE SET
                    track_length_feet = excluded.track_length_feet";
            command.Parameters.AddWithValue("$trackLengthFeet", trackLengthFeet);
            command.ExecuteNonQuery();
        }

        public static int LoadSensorDebounceMilliseconds(int defaultValue)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"SELECT debounce_milliseconds FROM controller_settings WHERE id = 1";
            object? value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? defaultValue
                : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static void SaveSensorDebounceMilliseconds(int debounceMilliseconds)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO controller_settings (
                    id, debounce_milliseconds, raw_sensor_lockout_milliseconds)
                VALUES (1, $debounceMilliseconds, $defaultRawSensorLockoutMilliseconds)
                ON CONFLICT(id) DO UPDATE SET
                    debounce_milliseconds = excluded.debounce_milliseconds";
            command.Parameters.AddWithValue("$debounceMilliseconds", debounceMilliseconds);
            command.Parameters.AddWithValue("$defaultRawSensorLockoutMilliseconds", DefaultRawSensorLockoutMilliseconds);
            command.ExecuteNonQuery();
        }

        public static int LoadRawSensorLockoutMilliseconds(int defaultValue)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"SELECT raw_sensor_lockout_milliseconds FROM controller_settings WHERE id = 1";
            object? value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? defaultValue
                : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static void SaveRawSensorLockoutMilliseconds(int rawSensorLockoutMilliseconds)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO controller_settings (
                    id, debounce_milliseconds, raw_sensor_lockout_milliseconds)
                VALUES (1, $defaultSensorDebounceMilliseconds, $rawSensorLockoutMilliseconds)
                ON CONFLICT(id) DO UPDATE SET
                    raw_sensor_lockout_milliseconds = excluded.raw_sensor_lockout_milliseconds";
            command.Parameters.AddWithValue("$defaultSensorDebounceMilliseconds", DefaultSensorDebounceMilliseconds);
            command.Parameters.AddWithValue("$rawSensorLockoutMilliseconds", rawSensorLockoutMilliseconds);
            command.ExecuteNonQuery();
        }

        public static QualifyingSetupSettings LoadQualifyingSettings(QualifyingSetupSettings defaults)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"
                SELECT lane_index, duration_seconds
                FROM qualifying_settings
                WHERE id = 1";
            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Read()
                ? new QualifyingSetupSettings(reader.GetInt32(0), reader.GetInt32(1))
                : defaults;
        }

        public static void SaveQualifyingSettings(QualifyingSetupSettings settings)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO qualifying_settings (id, lane_index, duration_seconds)
                VALUES (1, $laneIndex, $durationSeconds)
                ON CONFLICT(id) DO UPDATE SET
                    lane_index = excluded.lane_index,
                    duration_seconds = excluded.duration_seconds";
            command.Parameters.AddWithValue("$laneIndex", settings.LaneIndex);
            command.Parameters.AddWithValue("$durationSeconds", settings.DurationSeconds);
            command.ExecuteNonQuery();
        }

        public static IReadOnlyList<LaneConfiguration> LoadLaneConfigurations(
            IReadOnlyList<LaneConfiguration> defaults)
        {
            LaneConfiguration[] lanes = defaults.ToArray();
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"
                SELECT lane_index, display_name, color_argb
                FROM lane_settings
                ORDER BY lane_index";

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                int laneIndex = reader.GetInt32(0);
                if (laneIndex < 0 || laneIndex >= lanes.Length)
                {
                    continue;
                }

                string name = reader.IsDBNull(1) ? lanes[laneIndex].Name : reader.GetString(1).Trim();
                int colorArgb = reader.IsDBNull(2) ? lanes[laneIndex].ColorArgb : reader.GetInt32(2);
                lanes[laneIndex] = new LaneConfiguration(
                    string.IsNullOrWhiteSpace(name) ? lanes[laneIndex].Name : name,
                    colorArgb);
            }

            return lanes;
        }

        public static void SaveLaneConfigurations(IReadOnlyList<LaneConfiguration> lanes)
        {
            using SqliteTransaction transaction = Connection.BeginTransaction();
            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                LaneConfiguration lane = lanes[laneIndex];
                using SqliteCommand command = Connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO lane_settings (lane_index, display_name, color_argb)
                    VALUES ($laneIndex, $displayName, $colorArgb)
                    ON CONFLICT(lane_index) DO UPDATE SET
                        display_name = excluded.display_name,
                        color_argb = excluded.color_argb";
                command.Parameters.AddWithValue("$laneIndex", laneIndex);
                command.Parameters.AddWithValue("$displayName", lane.Name.Trim());
                command.Parameters.AddWithValue("$colorArgb", lane.ColorArgb);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        public static List<string> LoadRacerNames()
        {
            List<string> racerNames = new();
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"SELECT name FROM users ORDER BY name COLLATE NOCASE, name";

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                {
                    string name = reader.GetString(0).Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        racerNames.Add(name);
                    }
                }
            }

            return racerNames;
        }

        public static void SaveRacerNames(IEnumerable<string> names)
        {
            List<string> desiredNames = names
                .Select(name => name.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(name => name, StringComparer.Ordinal)
                .ToList();

            using SqliteTransaction transaction = Connection.BeginTransaction();
            using SqliteCommand delete = Connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = @"DELETE FROM users WHERE name NOT IN (" +
                string.Join(",", desiredNames.Select((_, i) => $"$name{i}")) + ")";

            if (desiredNames.Count == 0)
            {
                delete.CommandText = @"DELETE FROM users";
            }
            else
            {
                for (int i = 0; i < desiredNames.Count; i++)
                {
                    delete.Parameters.AddWithValue($"$name{i}", desiredNames[i]);
                }
            }

            delete.ExecuteNonQuery();

            foreach (string name in desiredNames)
            {
                using SqliteCommand insert = Connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"
                    INSERT INTO users (name)
                    SELECT $name
                    WHERE NOT EXISTS (
                        SELECT 1 FROM users WHERE name = $name COLLATE NOCASE
                    )";
                insert.Parameters.AddWithValue("$name", name);
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        public static HeatRaceSetupSettings LoadHeatRaceSettings(HeatRaceSetupSettings defaults)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"
                SELECT heat_length_minutes, between_heats_seconds
                FROM heat_race_settings
                WHERE id = 1";

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return defaults;
            }

            int heatLengthMinutes = reader.IsDBNull(0) ? defaults.HeatLengthMinutes : reader.GetInt32(0);
            int betweenHeatsSeconds = reader.IsDBNull(1) ? defaults.BetweenHeatsSeconds : reader.GetInt32(1);
            reader.Close();
            using SqliteCommand raceNameCommand = Connection.CreateCommand();
            raceNameCommand.CommandText = @"SELECT race_name FROM heat_race_identity WHERE id = 1";
            string raceName = raceNameCommand.ExecuteScalar()?.ToString()?.Trim() ?? defaults.RaceName;
            return new HeatRaceSetupSettings(heatLengthMinutes, betweenHeatsSeconds, raceName);
        }

        public static void SaveHeatRaceSettings(HeatRaceSetupSettings settings)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO heat_race_settings (id, heat_length_minutes, between_heats_seconds)
                VALUES (1, $heatLengthMinutes, $betweenHeatsSeconds)
                ON CONFLICT(id) DO UPDATE SET
                    heat_length_minutes = excluded.heat_length_minutes,
                    between_heats_seconds = excluded.between_heats_seconds";
            command.Parameters.AddWithValue("$heatLengthMinutes", settings.HeatLengthMinutes);
            command.Parameters.AddWithValue("$betweenHeatsSeconds", settings.BetweenHeatsSeconds);
            command.ExecuteNonQuery();

            using SqliteCommand raceNameCommand = Connection.CreateCommand();
            raceNameCommand.CommandText = @"
                INSERT INTO heat_race_identity (id, race_name)
                VALUES (1, $raceName)
                ON CONFLICT(id) DO UPDATE SET race_name = excluded.race_name";
            raceNameCommand.Parameters.AddWithValue("$raceName", settings.RaceName.Trim());
            raceNameCommand.ExecuteNonQuery();
        }

        private static void EnsureSchema()
        {
            ExecuteNonQuery(@"
                CREATE TABLE IF NOT EXISTS users (
                    name TEXT NOT NULL
                )");
            ExecuteNonQuery(@"
                CREATE TABLE IF NOT EXISTS comports (
                    name TEXT NOT NULL
                )");
            ExecuteNonQuery(@"
                CREATE TABLE IF NOT EXISTS heat_race_settings (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    heat_length_minutes INTEGER NOT NULL,
                    between_heats_seconds INTEGER NOT NULL
                )");
            ExecuteNonQuery(@"
                CREATE TABLE IF NOT EXISTS app_settings (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    min_lap_milliseconds INTEGER NOT NULL,
                    sound_on_too_fast_lap INTEGER NOT NULL,
                    voice_announcements_enabled INTEGER NOT NULL DEFAULT 1,
                    speech_voice_name TEXT NOT NULL,
                    speech_backend TEXT NOT NULL DEFAULT 'Automatic',
                    active_lane_count INTEGER NOT NULL
                )");
            ExecuteNonQuery(@"
                CREATE TABLE IF NOT EXISTS lane_settings (
                    lane_index INTEGER PRIMARY KEY CHECK (lane_index BETWEEN 0 AND 7),
                    display_name TEXT NOT NULL,
                    color_argb INTEGER NOT NULL
                )");
            ExecuteNonQuery(@"
                CREATE TABLE IF NOT EXISTS race_report_settings (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    export_json INTEGER NOT NULL,
                    export_csv INTEGER NOT NULL
                )");
            ExecuteNonQuery(@"
                CREATE TABLE IF NOT EXISTS heat_race_identity (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    race_name TEXT NOT NULL
                )");
            ExecuteNonQuery(@"
                CREATE TABLE IF NOT EXISTS track_configuration (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    track_length_feet REAL NOT NULL
                )");
            ExecuteNonQuery(@"
                CREATE TABLE IF NOT EXISTS qualifying_settings (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    lane_index INTEGER NOT NULL,
                    duration_seconds INTEGER NOT NULL
                )");
            ExecuteNonQuery(@"
                CREATE TABLE IF NOT EXISTS controller_settings (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    debounce_milliseconds INTEGER NOT NULL
                )");
            EnsureAppVoiceAnnouncementsColumn();
            EnsureAppSpeechBackendColumn();
            EnsureControllerRawSensorLockoutColumn();
            ExecuteNonQuery($"PRAGMA user_version = {CurrentSchemaVersion}");
        }

        private static void EnsureAppVoiceAnnouncementsColumn()
        {
            EnsureColumn(
                "app_settings",
                "voice_announcements_enabled",
                "ALTER TABLE app_settings " +
                "ADD COLUMN voice_announcements_enabled INTEGER NOT NULL DEFAULT 1");
        }

        private static void EnsureAppSpeechBackendColumn()
        {
            EnsureColumn(
                "app_settings",
                "speech_backend",
                "ALTER TABLE app_settings " +
                "ADD COLUMN speech_backend TEXT NOT NULL DEFAULT 'Automatic'");
        }

        private static void EnsureControllerRawSensorLockoutColumn()
        {
            EnsureColumn(
                "controller_settings",
                "raw_sensor_lockout_milliseconds",
                "ALTER TABLE controller_settings " +
                "ADD COLUMN raw_sensor_lockout_milliseconds INTEGER");
        }

        private static void EnsureColumn(string tableName, string columnName, string alterCommand)
        {
            using (SqliteCommand checkCommand = Connection.CreateCommand())
            {
                checkCommand.CommandText = $"PRAGMA table_info({tableName});";
                using SqliteDataReader reader = checkCommand.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(
                        reader.GetString(1),
                        columnName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }

            ExecuteNonQuery(alterCommand);
        }

        private static void ExecuteNonQuery(string commandText)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = commandText;
            command.ExecuteNonQuery();
        }

    }
}
