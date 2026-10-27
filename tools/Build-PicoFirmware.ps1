[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
& (Join-Path $PSScriptRoot "Sync-ControllerCore.ps1") -Check
$cores = & arduino-cli core list --format json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "Cannot inspect Arduino cores." }
$pico = $cores.platforms | Where-Object { $_.id -eq "rp2040:rp2040" }
if (-not $pico -or $pico.installed_version -ne "6.3.1") {
    throw "Install rp2040:rp2040@6.3.1 using the Arduino-Pico package index; see YATSSPico/README.md."
}
$output = Join-Path $root "artifacts\pico2w"
& arduino-cli compile --fqbn rp2040:rp2040:rpipico2w --output-dir $output (Join-Path $root "YATSSPico")
if ($LASTEXITCODE -ne 0) { throw "Pico 2 W firmware compilation failed." }
$uf2 = Join-Path $output "YATSSPico.ino.uf2"
if (-not (Test-Path -LiteralPath $uf2)) { throw "Compiler did not produce the expected UF2 image." }
Get-FileHash -LiteralPath $uf2 -Algorithm SHA256
Write-Host "Compile only; no board flashed. UF2: $uf2"
