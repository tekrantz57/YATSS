using System.IO.Compression;
using System.Security.Cryptography;

namespace YATSS
{
    public static class EspToolProvider
    {
        public const string OfficialVersion = "5.3.0";
        public const string OfficialDownloadUrl =
            "https://github.com/espressif/esptool/releases/download/v5.3.0/" +
            "esptool-v5.3.0-windows-amd64.zip";
        public const string OfficialArchiveSha256 =
            "C86E30586E559F0AE5B41A828F21B4C69A7FC11A194E09391F5A2E31952B2471";
        public const long OfficialArchiveBytes = 63_716_106;

        private static readonly HttpClient DownloadClient = CreateDownloadClient();

        public static bool TryFindEspTool(out string path, string? localApplicationData = null)
        {
            string? configuredPath = Environment.GetEnvironmentVariable("YATSS_ESPTOOL_PATH");
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                path = Path.GetFullPath(configuredPath);
                return true;
            }

            string applicationData = localApplicationData ??
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string cachedPath = GetCachedEspToolPath(applicationData);
            if (File.Exists(cachedPath))
            {
                path = cachedPath;
                return true;
            }

            string arduinoToolRoot = Path.Combine(
                applicationData,
                "Arduino15",
                "packages",
                "esp32",
                "tools",
                "esptool_py");
            if (Directory.Exists(arduinoToolRoot))
            {
                string? installedPath = Directory.GetDirectories(arduinoToolRoot)
                    .Select(directory => new
                    {
                        Path = Path.Combine(directory, "esptool.exe"),
                        Version = ParseVersion(Path.GetFileName(directory))
                    })
                    .Where(candidate => File.Exists(candidate.Path))
                    .OrderByDescending(candidate => candidate.Version)
                    .Select(candidate => candidate.Path)
                    .FirstOrDefault();
                if (installedPath != null)
                {
                    path = installedPath;
                    return true;
                }
            }

            path = string.Empty;
            return false;
        }

        public static async Task<string> DownloadOfficialEspToolAsync(
            IProgress<string>? progress = null,
            string? localApplicationData = null,
            CancellationToken cancellationToken = default)
        {
            string applicationData = localApplicationData ??
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string destinationPath = GetCachedEspToolPath(applicationData);
            if (File.Exists(destinationPath))
            {
                return destinationPath;
            }

            string destinationDirectory = Path.GetDirectoryName(destinationPath)!;
            Directory.CreateDirectory(destinationDirectory);
            string archivePath = Path.Combine(destinationDirectory, $"download-{Guid.NewGuid():N}.zip");
            string temporaryExecutable = destinationPath + $".{Guid.NewGuid():N}.tmp";

            try
            {
                progress?.Report($"Downloading official Espressif esptool {OfficialVersion}...");
                using HttpResponseMessage response = await DownloadClient.GetAsync(
                    OfficialDownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? OfficialArchiveBytes;
                await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (FileStream destination = new(
                    archivePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 128,
                    useAsync: true))
                {
                    byte[] buffer = new byte[1024 * 128];
                    long received = 0;
                    long lastReportedPercent = -1;
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        received += read;
                        if (totalBytes > 0)
                        {
                            long percent = Math.Min(100, received * 100 / totalBytes);
                            if (percent != lastReportedPercent)
                            {
                                progress?.Report($"Downloaded {percent}%");
                                lastReportedPercent = percent;
                            }
                        }
                    }
                }

                string actualHash = Convert.ToHexString(await HashFileAsync(archivePath, cancellationToken));
                if (!string.Equals(actualHash, OfficialArchiveSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "The downloaded esptool archive did not match Espressif's published SHA-256");
                }

                progress?.Report("Download verified; installing uploader...");
                using ZipArchive archive = ZipFile.OpenRead(archivePath);
                ZipArchiveEntry executableEntry = archive.Entries.SingleOrDefault(entry =>
                    string.Equals(entry.Name, "esptool.exe", StringComparison.OrdinalIgnoreCase)) ??
                    throw new InvalidDataException("The official esptool archive does not contain esptool.exe");
                executableEntry.ExtractToFile(temporaryExecutable, overwrite: false);
                File.Move(temporaryExecutable, destinationPath, overwrite: true);

                string sourceNote = Path.Combine(destinationDirectory, "SOURCE.txt");
                await File.WriteAllTextAsync(
                    sourceNote,
                    $"Downloaded by YATSS from:{Environment.NewLine}{OfficialDownloadUrl}{Environment.NewLine}" +
                    $"SHA-256: {OfficialArchiveSha256}{Environment.NewLine}" +
                    "License and source: https://github.com/espressif/esptool",
                    cancellationToken);
                return destinationPath;
            }
            finally
            {
                TryDelete(archivePath);
                TryDelete(temporaryExecutable);
            }
        }

        internal static string GetCachedEspToolPath(string localApplicationData) =>
            Path.Combine(localApplicationData, "YATSS", "Tools", "esptool", OfficialVersion, "esptool.exe");

        private static HttpClient CreateDownloadClient()
        {
            HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("YATSS-controller-updater/0.10");
            return client;
        }

        private static async Task<byte[]> HashFileAsync(string path, CancellationToken cancellationToken)
        {
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 128,
                useAsync: true);
            return await SHA256.HashDataAsync(stream, cancellationToken);
        }

        private static Version ParseVersion(string value) =>
            Version.TryParse(value, out Version? version) ? version : new Version(0, 0);

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
