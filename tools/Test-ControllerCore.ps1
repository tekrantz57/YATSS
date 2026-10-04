[CmdletBinding()]
param([string]$Compiler = "")
$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
& (Join-Path $PSScriptRoot "Sync-ControllerCore.ps1") -Check
$output = Join-Path $root "artifacts\controller-tests"
New-Item -ItemType Directory -Path $output -Force | Out-Null
if ($Compiler) {
    & $Compiler -std=c++17 -Wall -Wextra -Werror -pedantic (Join-Path $root "tests\controller\main.cpp") -o "$output\controller-tests.exe"
    if ($LASTEXITCODE -ne 0) { throw "Shared controller test compilation failed." }
    & (Join-Path $output "controller-tests.exe")
    if ($LASTEXITCODE -ne 0) { throw "Shared controller tests failed." }
    return
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -prerelease -products '*' -property installationPath
if (-not $vs) { throw "Install the Visual Studio C++ build tools to run native controller tests." }
& (Join-Path $PSScriptRoot "test-controller-msvc.cmd") $vs $root
if ($LASTEXITCODE -ne 0) { throw "Shared controller test compilation failed." }
& (Join-Path $output "controller-tests.exe")
if ($LASTEXITCODE -ne 0) { throw "Shared controller tests failed." }
