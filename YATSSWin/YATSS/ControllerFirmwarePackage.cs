using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace YATSS
{
    public sealed record ControllerFirmwareManifest(
        int FormatVersion,
        string Product,
        string FirmwareVersion,
        string BoardProfile,
        string BoardDisplayName,
        string Chip,
        string UploaderBackend,
        string ArduinoFqbn,
        string ArduinoCoreVersion,
        string ImageFile,
        long ImageSizeBytes,
        long FlashOffset,
        string Sha256);

    public sealed class ControllerFirmwarePackage
    {
        public const int CurrentFormatVersion = 1;
        public const string PackageExtension = ".yatssfw";
        public const string Esp32C6BoardProfile = "ESP32_C6_DEVKITC1";
        private const int MaximumManifestBytes = 64 * 1024;
        private const int MaximumImageBytes = 16 * 1024 * 1024;

        private ControllerFirmwarePackage(
            string packagePath,
            ControllerFirmwareManifest manifest,
            byte[] imageBytes)
        {
            PackagePath = packagePath;
            Manifest = manifest;
            ImageBytes = imageBytes;
        }

        public string PackagePath { get; }

        public ControllerFirmwareManifest Manifest { get; }

        public byte[] ImageBytes { get; }

        public static ControllerFirmwarePackage Load(string packagePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
            {
                throw new InvalidDataException("Firmware package path is required");
            }

            string fullPath = Path.GetFullPath(packagePath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Firmware package was not found", fullPath);
            }

            using ZipArchive archive = ZipFile.OpenRead(fullPath);
            ZipArchiveEntry manifestEntry = archive.GetEntry("manifest.json") ??
                throw new InvalidDataException("Firmware package does not contain manifest.json");
            if (manifestEntry.Length <= 0 || manifestEntry.Length > MaximumManifestBytes)
            {
                throw new InvalidDataException("Firmware manifest has an invalid size");
            }

            ControllerFirmwareManifest manifest;
            using (Stream manifestStream = manifestEntry.Open())
            {
                manifest = JsonSerializer.Deserialize<ControllerFirmwareManifest>(
                    manifestStream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
                    throw new InvalidDataException("Firmware manifest is empty");
            }

            ValidateManifest(manifest);
            ZipArchiveEntry imageEntry = archive.GetEntry(manifest.ImageFile) ??
                throw new InvalidDataException($"Firmware package does not contain {manifest.ImageFile}");
            if (imageEntry.Length != manifest.ImageSizeBytes ||
                imageEntry.Length <= 0 || imageEntry.Length > MaximumImageBytes)
            {
                throw new InvalidDataException("Firmware image size does not match its manifest");
            }

            byte[] imageBytes;
            using (Stream imageStream = imageEntry.Open())
            using (MemoryStream imageBuffer = new((int)imageEntry.Length))
            {
                imageStream.CopyTo(imageBuffer);
                imageBytes = imageBuffer.ToArray();
            }

            string actualHash = Convert.ToHexString(SHA256.HashData(imageBytes));
            if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Firmware image SHA-256 does not match its manifest");
            }

            return new ControllerFirmwarePackage(fullPath, manifest, imageBytes);
        }

        public static IReadOnlyList<ControllerFirmwarePackage> LoadBundledPackages(string? baseDirectory = null)
        {
            string firmwareDirectory = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "Firmware");
            if (!Directory.Exists(firmwareDirectory))
            {
                return Array.Empty<ControllerFirmwarePackage>();
            }

            return Directory.GetFiles(firmwareDirectory, $"*{PackageExtension}")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(Load)
                .ToArray();
        }

        public bool Matches(ControllerIdentity identity) =>
            string.Equals(
                Manifest.BoardProfile,
                identity.BoardProfile,
                StringComparison.OrdinalIgnoreCase);

        private static void ValidateManifest(ControllerFirmwareManifest manifest)
        {
            if (manifest.FormatVersion != CurrentFormatVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported firmware package format {manifest.FormatVersion}");
            }

            if (!string.Equals(manifest.Product, "YATSSMC", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Firmware package is not for YATSSMC");
            }

            if (string.IsNullOrWhiteSpace(manifest.FirmwareVersion) ||
                string.IsNullOrWhiteSpace(manifest.BoardDisplayName) ||
                string.IsNullOrWhiteSpace(manifest.ArduinoFqbn) ||
                string.IsNullOrWhiteSpace(manifest.ArduinoCoreVersion))
            {
                throw new InvalidDataException("Firmware manifest is missing required version or board information");
            }

            if (!string.Equals(manifest.BoardProfile, Esp32C6BoardProfile, StringComparison.Ordinal) ||
                !string.Equals(manifest.Chip, "esp32c6", StringComparison.Ordinal) ||
                !string.Equals(manifest.UploaderBackend, "esptool", StringComparison.Ordinal))
            {
                throw new InvalidDataException("This YATSS version supports firmware packages only for ESP32-C6-DevKitC-1");
            }

            if (manifest.FlashOffset != 0)
            {
                throw new InvalidDataException("The C6 firmware package must contain a merged image for flash offset 0");
            }

            if (string.IsNullOrWhiteSpace(manifest.ImageFile) ||
                Path.GetFileName(manifest.ImageFile) != manifest.ImageFile ||
                !manifest.ImageFile.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Firmware manifest contains an invalid image filename");
            }

            if (manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException("Firmware manifest contains an invalid SHA-256 value");
            }
        }
    }
}
