using System.Diagnostics;

namespace YATSS
{
    public sealed class Esp32C6FirmwareFlasher
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
                await RunEspToolAsync(
                    CreateProbeArguments(portName),
                    progress,
                    cancellationToken);

                progress?.Report($"Writing {package.Manifest.FirmwareVersion} to {portName}");
                await RunEspToolAsync(
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

        private async Task RunEspToolAsync(
            IReadOnlyList<string> arguments,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new(_esptoolPath)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new() { StartInfo = startInfo };
            List<string> output = new();
            object outputGate = new();
            void CaptureLine(string? line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                lock (outputGate)
                {
                    output.Add(line);
                }
                progress?.Report(line);
            }

            process.OutputDataReceived += (_, args) => CaptureLine(args.Data);
            process.ErrorDataReceived += (_, args) => CaptureLine(args.Data);
            if (!process.Start())
            {
                throw new InvalidOperationException("esptool did not start");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                string detail;
                lock (outputGate)
                {
                    detail = string.Join(Environment.NewLine, output.TakeLast(12));
                }
                throw new InvalidOperationException(
                    $"esptool failed with exit code {process.ExitCode}.{Environment.NewLine}{detail}");
            }
        }
    }
}
