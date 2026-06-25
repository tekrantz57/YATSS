using Microsoft.Data.Sqlite;

namespace tlp
{
    internal sealed record HeatRaceSetupSettings(int HeatLengthMinutes, int BetweenHeatsSeconds);

    internal static class AppDatabase
    {
        private const string ConnectionString = @"Data Source=c:\sqlite\data\laps.db";
        private static readonly object SyncRoot = new();

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

                    Connection.Open();
                }

                EnsureSchema();
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
            return new HeatRaceSetupSettings(heatLengthMinutes, betweenHeatsSeconds);
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
        }

        private static void ExecuteNonQuery(string commandText)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = commandText;
            command.ExecuteNonQuery();
        }
    }
}
