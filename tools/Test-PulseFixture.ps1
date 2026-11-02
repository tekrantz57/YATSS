[CmdletBinding()]
param([string]$Compiler = "")
$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$output = Join-Path $root "artifacts\pulse-fixture"
New-Item -ItemType Directory -Path $output -Force | Out-Null
if ($Compiler) {
    & $Compiler -std=c++17 -Wall -Wextra -Werror -pedantic (Join-Path $root "tests\pulse-fixture\main.cpp") -o "$output\pulse.exe"
} else {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    $vs = & $vswhere -latest -prerelease -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { throw "Visual Studio C++ build tools not found." }
    & (Join-Path $PSScriptRoot "test-pulse-fixture-msvc.cmd") $vs $root
}
if ($LASTEXITCODE -ne 0) { throw "Pulse fixture test build failed." }
& (Join-Path $output "pulse.exe")
if ($LASTEXITCODE -ne 0) { throw "Pulse fixture tests failed." }
