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
$nanoBuildDirectory = Join-Path $artifactRoot "nano-build"
$nanoStagingDirectory = Join-Path $artifactRoot "nano-package"
$nanoFqbn = "arduino:esp32:nano_nora:USBMode=default,PartitionScheme=default,PinNumbers=default"
$c5BuildDirectory = Join-Path $artifactRoot "c5-n16r8-build"
$c5StagingDirectory = Join-Path $artifactRoot "c5-n16r8-package"
$c5Fqbn = "esp32:esp32:esp32c5:CDCOnBoot=default,CPUFreq=240,FlashFreq=80,FlashMode=qio,FlashSize=16M,PartitionScheme=fatflash,PSRAM=enabled"
$c6Profiles = @(
    [pscustomobject]@{
        Label         = "n4"
        DisplayName   = "ESP32-C6-DevKitC-1 N4"
        CapacityBytes = 4194304
        Fqbn          = "esp32:esp32:esp32c6:CDCOnBoot=default,FlashSize=4M,PartitionScheme=default"
        BuildPath     = Join-Path $artifactRoot "c6-n4-build"
        StagingPath   = Join-Path $artifactRoot "c6-n4-package"
    },
    [pscustomobject]@{
        Label         = "n8"
        DisplayName   = "ESP32-C6-DevKitC-1 N8"
        CapacityBytes = 8388608
        Fqbn          = "esp32:esp32:esp32c6:CDCOnBoot=default,FlashSize=8M,PartitionScheme=default_8MB"
        BuildPath     = Join-Path $artifactRoot "c6-n8-build"
        StagingPath   = Join-Path $artifactRoot "c6-n8-package"
    }
)

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

$espCoreLine = $coreList | Where-Object { $_ -match '^esp32:esp32\s+' } | Select-Object -First 1
if ($null -eq $espCoreLine -or $espCoreLine -notmatch '^esp32:esp32\s+(?<version>\S+)') {
    throw "The Espressif ESP32 Arduino core is not installed"
}
$espCoreVersion = $Matches["version"]

$nanoCoreLine = $coreList | Where-Object { $_ -match '^arduino:esp32\s+' } | Select-Object -First 1
if ($null -eq $nanoCoreLine -or $nanoCoreLine -notmatch '^arduino:esp32\s+(?<version>\S+)') {
    throw "The Arduino ESP32 board core is not installed"
}
$nanoCoreVersion = $Matches["version"]

$generatedPaths = @(
    $nanoBuildDirectory,
    $nanoStagingDirectory,
    $c5BuildDirectory,
    $c5StagingDirectory) +
    @($c6Profiles | ForEach-Object { $_.BuildPath; $_.StagingPath })
foreach ($path in $generatedPaths) {
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
    "YATSSMC-esp32-c5-waveshare-wifi6-n16r8-*.yatssfw",
    "YATSSMC-arduino-nano-esp32-*.yatssfw")) {
    Get-ChildItem -LiteralPath $OutputDirectory -Filter $pattern -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

$results = @()
foreach ($profile in $c6Profiles) {
    & $ArduinoCli compile `
        --fqbn $profile.Fqbn `
        --build-path $profile.BuildPath `
        $sketchDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "ESP32-C6 $($profile.Label.ToUpperInvariant()) firmware compilation failed"
    }

    $mergedImage = Join-Path $profile.BuildPath "YATSSMC.ino.merged.bin"
    if (-not (Test-Path -LiteralPath $mergedImage -PathType Leaf)) {
        throw "Arduino did not produce the merged C6 $($profile.Label.ToUpperInvariant()) firmware image"
    }

    $imageName = "YATSSMC-esp32-c6-devkitc1-$($profile.Label)-$safeVersion.bin"
    $stagedImage = Join-Path $profile.StagingPath $imageName
    Copy-Item -LiteralPath $mergedImage -Destination $stagedImage
    $image = Get-Item -LiteralPath $stagedImage
    if ($image.Length -ne $profile.CapacityBytes) {
        throw "C6 $($profile.Label.ToUpperInvariant()) merged image size does not match its flash capacity"
    }
    $sha256 = (Get-FileHash -LiteralPath $stagedImage -Algorithm SHA256).Hash

    $manifest = [ordered]@{
        formatVersion      = 2
        product            = "YATSSMC"
        firmwareVersion    = $firmwareVersion
        boardProfile       = "ESP32_C6_DEVKITC1"
        boardDisplayName   = $profile.DisplayName
        chip               = "esp32c6"
        uploaderBackend    = "esptool"
        arduinoFqbn         = $profile.Fqbn
        arduinoCoreVersion = $espCoreVersion
        imageFile          = $imageName
        imageSizeBytes     = $image.Length
        flashOffset        = 0
        sha256             = $sha256
        flashCapacityBytes = $profile.CapacityBytes
    }
    $manifest | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $profile.StagingPath "manifest.json") -Encoding utf8

    $packageName = "YATSSMC-esp32-c6-devkitc1-$($profile.Label)-$safeVersion.yatssfw"
    $packagePath = Join-Path ([System.IO.Path]::GetFullPath($OutputDirectory)) $packageName
    $temporaryZip = [System.IO.Path]::ChangeExtension($packagePath, ".zip")
    Remove-Item -LiteralPath $packagePath, $temporaryZip -Force -ErrorAction SilentlyContinue
    Compress-Archive `
        -Path (Join-Path $profile.StagingPath "*") `
        -DestinationPath $temporaryZip `
        -CompressionLevel Optimal
    Move-Item -LiteralPath $temporaryZip -Destination $packagePath

    $results += [pscustomobject]@{
        Package         = $packagePath
        FirmwareVersion = $firmwareVersion
        ArduinoCore     = $espCoreVersion
        ImageBytes      = $image.Length
        PackageBytes    = (Get-Item -LiteralPath $packagePath).Length
        SHA256          = $sha256
    }
}

& $ArduinoCli compile `
    --fqbn $c5Fqbn `
    --build-path $c5BuildDirectory `
    $sketchDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Waveshare ESP32-C5 N16R8 firmware compilation failed"
}

$c5MergedImage = Join-Path $c5BuildDirectory "YATSSMC.ino.merged.bin"
if (-not (Test-Path -LiteralPath $c5MergedImage -PathType Leaf)) {
    throw "Arduino did not produce the merged C5 N16R8 firmware image"
}

$c5CapacityBytes = 16777216
$c5ImageName = "YATSSMC-esp32-c5-waveshare-wifi6-n16r8-$safeVersion.bin"
$c5StagedImage = Join-Path $c5StagingDirectory $c5ImageName
Copy-Item -LiteralPath $c5MergedImage -Destination $c5StagedImage
$c5Image = Get-Item -LiteralPath $c5StagedImage
if ($c5Image.Length -ne $c5CapacityBytes) {
    throw "C5 N16R8 merged image size does not match its flash capacity"
}
$c5Sha256 = (Get-FileHash -LiteralPath $c5StagedImage -Algorithm SHA256).Hash
$c5Manifest = [ordered]@{
    formatVersion      = 2
    product            = "YATSSMC"
    firmwareVersion    = $firmwareVersion
    boardProfile       = "ESP32_C5_WAVESHARE_WIFI6_N16R8"
    boardDisplayName   = "Waveshare ESP32-C5-WIFI6-KIT N16R8"
    chip               = "esp32c5"
    uploaderBackend    = "esptool"
    arduinoFqbn         = $c5Fqbn
    arduinoCoreVersion = $espCoreVersion
    imageFile          = $c5ImageName
    imageSizeBytes     = $c5Image.Length
    flashOffset        = 0
    sha256             = $c5Sha256
    flashCapacityBytes = $c5CapacityBytes
}
$c5Manifest | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $c5StagingDirectory "manifest.json") -Encoding utf8

$c5PackageName = "YATSSMC-esp32-c5-waveshare-wifi6-n16r8-$safeVersion.yatssfw"
$c5PackagePath = Join-Path ([System.IO.Path]::GetFullPath($OutputDirectory)) $c5PackageName
$c5TemporaryZip = [System.IO.Path]::ChangeExtension($c5PackagePath, ".zip")
Remove-Item -LiteralPath $c5PackagePath, $c5TemporaryZip -Force -ErrorAction SilentlyContinue
Compress-Archive `
    -Path (Join-Path $c5StagingDirectory "*") `
    -DestinationPath $c5TemporaryZip `
    -CompressionLevel Optimal
Move-Item -LiteralPath $c5TemporaryZip -Destination $c5PackagePath

$results += [pscustomobject]@{
    Package         = $c5PackagePath
    FirmwareVersion = $firmwareVersion
    ArduinoCore     = $espCoreVersion
    ImageBytes      = $c5Image.Length
    PackageBytes    = (Get-Item -LiteralPath $c5PackagePath).Length
    SHA256          = $c5Sha256
}

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
