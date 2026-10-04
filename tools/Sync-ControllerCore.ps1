[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$files = @("YatssController.h", "FirmwareVersion.h")
foreach ($folder in @("YATSSMC\src\YatssController", "YATSSUnoQ\sketch\src\YatssController")) {
    foreach ($file in $files) {
        $source = Join-Path $root "Controller\$file"
        $target = Join-Path $root "$folder\$file"
        if ($Check) {
            if (-not (Test-Path -LiteralPath $target) -or
                (Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash) {
                throw "Shared controller copy is stale: $target. Run tools/Sync-ControllerCore.ps1."
            }
        } else {
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $target -Force
        }
    }
}
# Keep the historical version-header path usable by firmware packagers.
$versionTarget = Join-Path $root "YATSSMC\FirmwareVersion.h"
$versionSource = Join-Path $root "Controller\FirmwareVersion.h"
if ($Check) {
    if ((Get-FileHash -LiteralPath $versionTarget).Hash -ne (Get-FileHash -LiteralPath $versionSource).Hash) {
        throw "YATSSMC/FirmwareVersion.h is stale. Run tools/Sync-ControllerCore.ps1."
    }
} else {
    Copy-Item -LiteralPath $versionSource -Destination $versionTarget -Force
}
Write-Host "Shared controller sources are synchronized."
