namespace YATSS
{
    public sealed class ArduinoNanoFirmwareFlasher : IControllerFirmwareFlasher
    {
        private readonly string _dfuUtilPath;

        public ArduinoNanoFirmwareFlasher(string dfuUtilPath)
        {
            if (string.IsNullOrWhiteSpace(dfuUtilPath) || !File.Exists(dfuUtilPath))
            {
                throw new FileNotFoundException("dfu-util.exe was not found", dfuUtilPath);
            }

            _dfuUtilPath = Path.GetFullPath(dfuUtilPath);
        }

        public async Task FlashAsync(
            ControllerFirmwarePackage package,
            string portName,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(package);
            if (!string.Equals(
                    package.Manifest.BoardProfile,
                    ControllerFirmwarePackage.ArduinoNanoEsp32BoardProfile,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The selected package is not supported by the Nano flasher");
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
                progress?.Report($"Using {Path.GetFileName(_dfuUtilPath)}");
                progress?.Report("Looking for Arduino Nano ESP32 DFU interface");
                await FirmwareToolRunner.RunAsync(
                    _dfuUtilPath,
                    CreateFlashArguments(package.Manifest, imagePath),
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

        internal static IReadOnlyList<string> CreateFlashArguments(
            ControllerFirmwareManifest manifest,
            string imagePath) =>
            new[]
            {
                "--device", $"{manifest.UsbVendorId}:{manifest.UsbProductId}",
                "-D", imagePath,
                "-Q"
            };
    }
}
