namespace YATSS
{
    public sealed class Esp32C6FirmwareFlasher : IControllerFirmwareFlasher
    {
        private const int FlashBaud = 460800;
        private readonly string _esptoolPath;

        public Esp32C6FirmwareFlasher(string esptoolPath)
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

            if (!string.Equals(
                    package.Manifest.BoardProfile,
                    ControllerFirmwarePackage.Esp32C6BoardProfile,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The selected package is not supported by the C6 flasher");
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
                progress?.Report($"Checking ESP32-C6 on {portName}");
                await FirmwareToolRunner.RunAsync(
                    _esptoolPath,
                    CreateProbeArguments(portName),
                    progress,
                    cancellationToken);

                progress?.Report($"Writing {package.Manifest.FirmwareVersion} to {portName}");
                await FirmwareToolRunner.RunAsync(
                    _esptoolPath,
                    CreateFlashArguments(portName, imagePath),
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

        internal static IReadOnlyList<string> CreateProbeArguments(string portName) =>
            new[]
            {
                "--chip", "esp32c6",
                "--port", portName,
                "--before", "default-reset",
                "--after", "hard-reset",
                "flash-id"
            };

        internal static IReadOnlyList<string> CreateFlashArguments(string portName, string imagePath) =>
            new[]
            {
                "--chip", "esp32c6",
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

    }
}
