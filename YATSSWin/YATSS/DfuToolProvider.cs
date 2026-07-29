using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace YATSS
{
    public static class DfuToolProvider
    {
        public const string OfficialVersion = "0.11.0-arduino5";
        public const string OfficialDownloadUrl =
            "https://downloads.arduino.cc/tools/dfu-util-0.11-arduino5-windows_386.tar.gz";
        public const string OfficialArchiveSha256 =
            "6451E16BF77600FE2436C8708AB4B75077C49997CF8BEDF03221D9D6726BB641";
        public const long OfficialArchiveBytes = 571_340;

        private static readonly HttpClient DownloadClient = CreateDownloadClient();

        public static bool TryFindDfuUtil(out string path, string? localApplicationData = null)
        {
            string? configuredPath = Environment.GetEnvironmentVariable("YATSS_DFU_UTIL_PATH");
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                path = Path.GetFullPath(configuredPath);
                return true;
            }

            string applicationData = localApplicationData ??
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string cachedPath = GetCachedDfuUtilPath(applicationData);
            if (File.Exists(cachedPath))
            {
                path = cachedPath;
                return true;
            }

            string arduinoToolRoot = Path.Combine(
                applicationData,
                "Arduino15",
                "packages",
                "arduino",
                "tools",
                "dfu-util");
            if (Directory.Exists(arduinoToolRoot))
            {
                string? installedPath = Directory.GetDirectories(arduinoToolRoot)
                    .Select(directory => new
                    {
                        Path = Path.Combine(directory, "dfu-util.exe"),
                        Version = ParseVersion(Path.GetFileName(directory))
                    })
                    .Where(candidate => File.Exists(candidate.Path))
                    .OrderByDescending(candidate => candidate.Version)
                    .ThenByDescending(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
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

        public static async Task<string> DownloadOfficialDfuUtilAsync(
            IProgress<string>? progress = null,
            string? localApplicationData = null,
            CancellationToken cancellationToken = default)
        {
            string applicationData = localApplicationData ??
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string destinationPath = GetCachedDfuUtilPath(applicationData);
            if (File.Exists(destinationPath))
            {
                return destinationPath;
            }

            string destinationDirectory = Path.GetDirectoryName(destinationPath)!;
            Directory.CreateDirectory(destinationDirectory);
            string archivePath = Path.Combine(destinationDirectory, $"download-{Guid.NewGuid():N}.tar.gz");
            string temporaryExecutable = destinationPath + $".{Guid.NewGuid():N}.tmp";

            try
            {
                progress?.Report($"Downloading official Arduino DFU utility {OfficialVersion}...");
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
                    1024 * 64,
                    useAsync: true))
                {
                    byte[] buffer = new byte[1024 * 64];
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
                        "The downloaded DFU utility did not match Arduino's published SHA-256");
                }

                progress?.Report("Download verified; installing DFU utility...");
                await ExtractExecutableAsync(archivePath, temporaryExecutable, cancellationToken);
                File.Move(temporaryExecutable, destinationPath, overwrite: true);

                string sourceNote = Path.Combine(destinationDirectory, "SOURCE.txt");
                await File.WriteAllTextAsync(
                    sourceNote,
                    $"Downloaded by YATSS from:{Environment.NewLine}{OfficialDownloadUrl}{Environment.NewLine}" +
                    $"SHA-256: {OfficialArchiveSha256}{Environment.NewLine}" +
                    "License and source: https://dfu-util.sourceforge.net/",
                    cancellationToken);
                return destinationPath;
            }
            finally
            {
                TryDelete(archivePath);
                TryDelete(temporaryExecutable);
            }
        }

        internal static string GetCachedDfuUtilPath(string localApplicationData) =>
            Path.Combine(localApplicationData, "YATSS", "Tools", "dfu-util", OfficialVersion, "dfu-util.exe");

        private static async Task ExtractExecutableAsync(
            string archivePath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            await using FileStream archiveStream = File.OpenRead(archivePath);
            await using GZipStream gzipStream = new(archiveStream, CompressionMode.Decompress);
            using TarReader reader = new(gzipStream);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) != null)
            {
                if (!string.Equals(Path.GetFileName(entry.Name), "dfu-util.exe", StringComparison.OrdinalIgnoreCase) ||
                    entry.DataStream == null)
                {
                    continue;
                }

                await using FileStream destination = new(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 64,
                    useAsync: true);
                await entry.DataStream.CopyToAsync(destination, cancellationToken);
                return;
            }

            throw new InvalidDataException("Arduino's DFU utility archive does not contain dfu-util.exe");
        }

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
                1024 * 64,
                useAsync: true);
            return await SHA256.HashDataAsync(stream, cancellationToken);
        }

        private static Version ParseVersion(string value)
        {
            string numeric = value.Split('-', 2)[0];
            return Version.TryParse(numeric, out Version? version) ? version : new Version(0, 0);
        }

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
