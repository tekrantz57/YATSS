namespace YATSS
{
    internal static class DatabaseFileMigration
    {
        public static string MoveLegacyDatabase(string legacyPath, string currentPath)
        {
            string legacyFullPath = Path.GetFullPath(legacyPath);
            string currentFullPath = Path.GetFullPath(currentPath);
            if (File.Exists(currentFullPath) || !File.Exists(legacyFullPath))
            {
                return currentFullPath;
            }

            string? directory = Path.GetDirectoryName(currentFullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string[] suffixes = { "", "-wal", "-shm" };
            List<(string Source, string Destination)> moved = new();
            try
            {
                foreach (string suffix in suffixes)
                {
                    string source = legacyFullPath + suffix;
                    string destination = currentFullPath + suffix;
                    if (!File.Exists(source))
                    {
                        continue;
                    }

                    if (File.Exists(destination))
                    {
                        throw new IOException($"Cannot rename the YATSS database because {destination} already exists.");
                    }

                    File.Move(source, destination);
                    moved.Add((source, destination));
                }
            }
            catch
            {
                for (int index = moved.Count - 1; index >= 0; index--)
                {
                    (string source, string destination) = moved[index];
                    if (File.Exists(destination) && !File.Exists(source))
                    {
                        File.Move(destination, source);
                    }
                }

                throw;
            }

            return currentFullPath;
        }
    }
}
