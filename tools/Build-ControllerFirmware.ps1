[CmdletBinding()]
param(
    [string]$ArduinoCli = "C:\Program Files\Arduino CLI\arduino-cli.exe",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$OutputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repositoryRoot "YATSSMC\dist"
} else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$sketchDirectory = Join-Path $repositoryRoot "YATSSMC"
$versionHeader = Join-Path $sketchDirectory "FirmwareVersion.h"
$artifactRoot = Join-Path $repositoryRoot "artifacts\controller-firmware"
$c6BuildDirectory = Join-Path $artifactRoot "c6-build"
$c6StagingDirectory = Join-Path $artifactRoot "c6-package"
$nanoBuildDirectory = Join-Path $artifactRoot "nano-build"
$nanoStagingDirectory = Join-Path $artifactRoot "nano-package"
$c6Fqbn = "esp32:esp32:esp32c6:CDCOnBoot=default,FlashSize=8M,PartitionScheme=default_8MB"
$nanoFqbn = "arduino:esp32:nano_nora:USBMode=default,PartitionScheme=default,PinNumbers=default"

if (-not (Test-Path -LiteralPath $ArduinoCli -PathType Leaf)) {
    throw "Arduino CLI was not found at $ArduinoCli"
}

$versionText = Get-Content -LiteralPath $versionHeader -Raw
$versionMatch = [regex]::Match(
    $versionText,
    '#define\s+YATSSMC_FIRMWARE_VERSION\s+"(?<version>[^"]+)"')
if (-not $versionMatch.Success) {
    throw "YATSSMC_FIRMWARE_VERSION was not found in $versionHeader"
}

$firmwareVersion = $versionMatch.Groups["version"].Value
$safeVersion = $firmwareVersion -replace '[^A-Za-z0-9._-]', '-'
$coreList = & $ArduinoCli core list
if ($LASTEXITCODE -ne 0) {
    throw "Arduino CLI could not list installed cores"
}

$c6CoreLine = $coreList | Where-Object { $_ -match '^esp32:esp32\s+' } | Select-Object -First 1
if ($null -eq $c6CoreLine -or $c6CoreLine -notmatch '^esp32:esp32\s+(?<version>\S+)') {
    throw "The Espressif ESP32 Arduino core is not installed"
}
$c6CoreVersion = $Matches["version"]

$nanoCoreLine = $coreList | Where-Object { $_ -match '^arduino:esp32\s+' } | Select-Object -First 1
if ($null -eq $nanoCoreLine -or $nanoCoreLine -notmatch '^arduino:esp32\s+(?<version>\S+)') {
    throw "The Arduino ESP32 board core is not installed"
}
$nanoCoreVersion = $Matches["version"]

foreach ($path in @($c6BuildDirectory, $c6StagingDirectory, $nanoBuildDirectory, $nanoStagingDirectory)) {
    if (Test-Path -LiteralPath $path) {
        $resolved = [System.IO.Path]::GetFullPath($path)
        if (-not $resolved.StartsWith($artifactRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unexpected path $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    New-Item -ItemType Directory -Path $path | Out-Null
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($pattern in @(
    "YATSSMC-esp32-c6-devkitc1-*.yatssfw",
    "YATSSMC-arduino-nano-esp32-*.yatssfw")) {
    Get-ChildItem -LiteralPath $OutputDirectory -Filter $pattern -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

& $ArduinoCli compile `
    --fqbn $c6Fqbn `
    --build-path $c6BuildDirectory `
    $sketchDirectory
if ($LASTEXITCODE -ne 0) {
    throw "ESP32-C6 firmware compilation failed"
}

$mergedImage = Join-Path $c6BuildDirectory "YATSSMC.ino.merged.bin"
if (-not (Test-Path -LiteralPath $mergedImage -PathType Leaf)) {
    throw "Arduino did not produce the merged C6 firmware image"
}

$imageName = "YATSSMC-esp32-c6-devkitc1-$safeVersion.bin"
$stagedImage = Join-Path $c6StagingDirectory $imageName
Copy-Item -LiteralPath $mergedImage -Destination $stagedImage
$image = Get-Item -LiteralPath $stagedImage
$sha256 = (Get-FileHash -LiteralPath $stagedImage -Algorithm SHA256).Hash

$manifest = [ordered]@{
    formatVersion      = 2
    product            = "YATSSMC"
    firmwareVersion    = $firmwareVersion
    boardProfile       = "ESP32_C6_DEVKITC1"
    boardDisplayName   = "ESP32-C6-DevKitC-1"
    chip               = "esp32c6"
    uploaderBackend    = "esptool"
    arduinoFqbn         = $c6Fqbn
    arduinoCoreVersion = $c6CoreVersion
    imageFile          = $imageName
    imageSizeBytes     = $image.Length
    flashOffset        = 0
    sha256             = $sha256
    flashCapacityBytes = 8388608
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $c6StagingDirectory "manifest.json") -Encoding utf8

$packageName = "YATSSMC-esp32-c6-devkitc1-$safeVersion.yatssfw"
$packagePath = Join-Path ([System.IO.Path]::GetFullPath($OutputDirectory)) $packageName
$temporaryZip = [System.IO.Path]::ChangeExtension($packagePath, ".zip")
Remove-Item -LiteralPath $packagePath, $temporaryZip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $c6StagingDirectory "*") -DestinationPath $temporaryZip -CompressionLevel Optimal
Move-Item -LiteralPath $temporaryZip -Destination $packagePath

$results = @([pscustomobject]@{
    Package         = $packagePath
    FirmwareVersion = $firmwareVersion
    ArduinoCore     = $c6CoreVersion
    ImageBytes      = $image.Length
    PackageBytes    = (Get-Item -LiteralPath $packagePath).Length
    SHA256          = $sha256
})

& $ArduinoCli compile `
    --fqbn $nanoFqbn `
    --build-path $nanoBuildDirectory `
    $sketchDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Arduino Nano ESP32 firmware compilation failed"
}

$nanoApplication = Join-Path $nanoBuildDirectory "YATSSMC.ino.bin"
if (-not (Test-Path -LiteralPath $nanoApplication -PathType Leaf)) {
    throw "Arduino did not produce the Nano ESP32 application image"
}

$nanoImageName = "YATSSMC-arduino-nano-esp32-$safeVersion.bin"
$nanoStagedImage = Join-Path $nanoStagingDirectory $nanoImageName
Copy-Item -LiteralPath $nanoApplication -Destination $nanoStagedImage
$nanoImage = Get-Item -LiteralPath $nanoStagedImage
$nanoSha256 = (Get-FileHash -LiteralPath $nanoStagedImage -Algorithm SHA256).Hash
$nanoManifest = [ordered]@{
    formatVersion      = 2
    product            = "YATSSMC"
    firmwareVersion    = $firmwareVersion
    boardProfile       = "ARDUINO_NANO_ESP32"
    boardDisplayName   = "Arduino Nano ESP32"
    chip               = "esp32s3"
    uploaderBackend    = "dfu-util"
    arduinoFqbn         = $nanoFqbn
    arduinoCoreVersion = $nanoCoreVersion
    imageFile          = $nanoImageName
    imageSizeBytes     = $nanoImage.Length
    flashOffset        = 0
    sha256             = $nanoSha256
    flashCapacityBytes = 16777216
    usbVendorId        = "2341"
    usbProductId       = "0070"
}
$nanoManifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $nanoStagingDirectory "manifest.json") -Encoding utf8

$nanoPackageName = "YATSSMC-arduino-nano-esp32-$safeVersion.yatssfw"
$nanoPackagePath = Join-Path ([System.IO.Path]::GetFullPath($OutputDirectory)) $nanoPackageName
$nanoTemporaryZip = [System.IO.Path]::ChangeExtension($nanoPackagePath, ".zip")
Remove-Item -LiteralPath $nanoPackagePath, $nanoTemporaryZip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $nanoStagingDirectory "*") -DestinationPath $nanoTemporaryZip -CompressionLevel Optimal
Move-Item -LiteralPath $nanoTemporaryZip -Destination $nanoPackagePath

$results += [pscustomobject]@{
    Package         = $nanoPackagePath
    FirmwareVersion = $firmwareVersion
    ArduinoCore     = $nanoCoreVersion
    ImageBytes      = $nanoImage.Length
    PackageBytes    = (Get-Item -LiteralPath $nanoPackagePath).Length
    SHA256          = $nanoSha256
}

$results
