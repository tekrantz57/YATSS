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
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw "Visual Studio Installer was not found. Install Desktop development with C++, or use -Compiler with a GCC-compatible compiler."
}
$vs = & $vswhere -latest -prerelease -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $vs) {
    throw "No Visual Studio installation has the C++ build tools installed. In Visual Studio Installer, Modify > Desktop development with C++; include the MSVC x64/x86 tools and a Windows SDK. Existing cl.exe files alone are not a complete toolchain."
}
& (Join-Path $PSScriptRoot "test-controller-msvc.cmd") $vs $root
if ($LASTEXITCODE -ne 0) { throw "Shared controller test compilation failed." }
& (Join-Path $output "controller-tests.exe")
if ($LASTEXITCODE -ne 0) { throw "Shared controller tests failed." }
