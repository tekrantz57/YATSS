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
$buildDirectory = Join-Path $artifactRoot "c6-build"
$stagingDirectory = Join-Path $artifactRoot "c6-package"
$fqbn = "esp32:esp32:esp32c6:CDCOnBoot=default,FlashSize=8M,PartitionScheme=default_8MB"

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

$coreLine = $coreList | Where-Object { $_ -match '^esp32:esp32\s+' } | Select-Object -First 1
if ($null -eq $coreLine -or $coreLine -notmatch '^esp32:esp32\s+(?<version>\S+)') {
    throw "The Espressif ESP32 Arduino core is not installed"
}
$coreVersion = $Matches["version"]

foreach ($path in @($buildDirectory, $stagingDirectory)) {
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

& $ArduinoCli compile `
    --fqbn $fqbn `
    --build-path $buildDirectory `
    $sketchDirectory
if ($LASTEXITCODE -ne 0) {
    throw "ESP32-C6 firmware compilation failed"
}

$mergedImage = Join-Path $buildDirectory "YATSSMC.ino.merged.bin"
if (-not (Test-Path -LiteralPath $mergedImage -PathType Leaf)) {
    throw "Arduino did not produce the merged C6 firmware image"
}

$imageName = "YATSSMC-esp32-c6-devkitc1-$safeVersion.bin"
$stagedImage = Join-Path $stagingDirectory $imageName
Copy-Item -LiteralPath $mergedImage -Destination $stagedImage
$image = Get-Item -LiteralPath $stagedImage
$sha256 = (Get-FileHash -LiteralPath $stagedImage -Algorithm SHA256).Hash

$manifest = [ordered]@{
    formatVersion      = 1
    product            = "YATSSMC"
    firmwareVersion    = $firmwareVersion
    boardProfile       = "ESP32_C6_DEVKITC1"
    boardDisplayName   = "ESP32-C6-DevKitC-1"
    chip               = "esp32c6"
    uploaderBackend    = "esptool"
    arduinoFqbn         = $fqbn
    arduinoCoreVersion = $coreVersion
    imageFile          = $imageName
    imageSizeBytes     = $image.Length
    flashOffset        = 0
    sha256             = $sha256
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stagingDirectory "manifest.json") -Encoding utf8

$packageName = "YATSSMC-esp32-c6-devkitc1-$safeVersion.yatssfw"
$packagePath = Join-Path ([System.IO.Path]::GetFullPath($OutputDirectory)) $packageName
$temporaryZip = [System.IO.Path]::ChangeExtension($packagePath, ".zip")
Remove-Item -LiteralPath $packagePath, $temporaryZip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stagingDirectory "*") -DestinationPath $temporaryZip -CompressionLevel Optimal
Move-Item -LiteralPath $temporaryZip -Destination $packagePath

[pscustomobject]@{
    Package         = $packagePath
    FirmwareVersion = $firmwareVersion
    ArduinoCore     = $coreVersion
    ImageBytes      = $image.Length
    PackageBytes    = (Get-Item -LiteralPath $packagePath).Length
    SHA256          = $sha256
}
