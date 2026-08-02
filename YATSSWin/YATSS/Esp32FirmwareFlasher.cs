namespace YATSS
{
    public sealed class Esp32FirmwareFlasher : IControllerFirmwareFlasher
    {
        private const int FlashBaud = 460800;
        private readonly string _esptoolPath;

        public Esp32FirmwareFlasher(string esptoolPath)
        {
            if (string.IsNullOrWhiteSpace(esptoolPath) || !File.Exists(esptoolPath))
            {
                throw new FileNotFoundException("esptool.exe was not found", esptoolPath);
            }

            _esptoolPath = Path.GetFullPath(esptoolPath);
        }

        public string EspToolPath => _esptoolPath;

        public async Task FlashAsync(
            ControllerFirmwarePackage package,
            string portName,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(package);
            if (string.IsNullOrWhiteSpace(portName))
            {
                throw new InvalidOperationException("Configure the controller COM port before updating firmware");
            }

            if (!string.Equals(package.Manifest.UploaderBackend, "esptool", StringComparison.Ordinal) ||
                package.Manifest.Chip is not ("esp32c5" or "esp32c6"))
            {
                throw new InvalidOperationException("The selected package is not supported by the ESP32 flasher");
            }

            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "YATSS",
                "FirmwareUpdate",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            string imagePath = Path.Combine(temporaryDirectory, package.Manifest.ImageFile);

            try
            {
                await File.WriteAllBytesAsync(imagePath, package.ImageBytes, cancellationToken);
                progress?.Report($"Using {Path.GetFileName(_esptoolPath)}");
                long detectedCapacity = await ProbeFlashCapacityAsync(
                    package.Manifest.Chip,
                    portName,
                    progress,
                    cancellationToken);
                if (detectedCapacity != package.Manifest.FlashCapacityBytes)
                {
                    throw new InvalidOperationException(
                        $"Connected controller has {FormatCapacity(detectedCapacity)} flash, but the selected " +
                        $"package requires {FormatCapacity(package.Manifest.FlashCapacityBytes)}");
                }

                progress?.Report($"Writing {package.Manifest.FirmwareVersion} to {portName}");
                await FirmwareToolRunner.RunAsync(
                    _esptoolPath,
                    CreateFlashArguments(package.Manifest.Chip, portName, imagePath),
                    progress,
                    cancellationToken);
            }
            finally
            {
                try
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
                catch
                {
                }
            }
        }

        public async Task<long> ProbeFlashCapacityAsync(
            string chip,
            string portName,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                throw new InvalidOperationException("Configure the controller COM port before inspecting firmware");
            }

            progress?.Report($"Checking {chip} flash capacity on {portName}");
            FirmwareToolResult result = await FirmwareToolRunner.RunAsync(
                _esptoolPath,
                CreateProbeArguments(chip, portName),
                progress,
                cancellationToken);
            return ParseFlashCapacity(result.OutputLines);
        }

        internal static IReadOnlyList<string> CreateProbeArguments(string chip, string portName) =>
            new[]
            {
                "--chip", chip,
                "--port", portName,
                "--before", "default-reset",
                "--after", "hard-reset",
                "flash-id"
            };

        internal static IReadOnlyList<string> CreateFlashArguments(
            string chip,
            string portName,
            string imagePath) =>
            new[]
            {
                "--chip", chip,
                "--port", portName,
                "--baud", FlashBaud.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--before", "default-reset",
                "--after", "hard-reset",
                "write-flash",
                "--flash-mode", "keep",
                "--flash-freq", "keep",
                "--flash-size", "keep",
                "0x0", imagePath
            };

        internal static long ParseFlashCapacity(IEnumerable<string> outputLines)
        {
            foreach (string line in outputLines)
            {
                System.Text.RegularExpressions.Match match =
                    System.Text.RegularExpressions.Regex.Match(
                        line,
                        @"flash\s+size:\s*(?<value>\d+)\s*(?<unit>KB|MB)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!match.Success ||
                    !long.TryParse(
                        match.Groups["value"].Value,
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out long value))
                {
                    continue;
                }

                return string.Equals(match.Groups["unit"].Value, "MB", StringComparison.OrdinalIgnoreCase)
                    ? value * 1024 * 1024
                    : value * 1024;
            }

            throw new InvalidOperationException("esptool did not report the connected controller flash capacity");
        }

        private static string FormatCapacity(long bytes) =>
            bytes % (1024 * 1024) == 0
                ? $"{bytes / (1024 * 1024)} MB"
                : $"{bytes} bytes";

    }
}
