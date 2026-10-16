[CmdletBinding()]
param([Parameter(Mandatory)][string]$Installer)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Installer = (Resolve-Path -LiteralPath $Installer).Path
if (-not $Installer.EndsWith('-test.exe', [StringComparison]::OrdinalIgnoreCase) -or
    [Diagnostics.FileVersionInfo]::GetVersionInfo($Installer).ProductName.Trim() -ne 'YATSS Installer Test') {
    throw 'Refusing a normal installer. Build with -TestInstaller first.'
}
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\YATSS.InstallerSmokeTest_is1'
if (Test-Path $key) { throw 'An installer test is already registered. Remove it deliberately before running another test.' }
$root = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $root ('artifacts\installer-tests\' + [Guid]::NewGuid().ToString('N'))
$installDir = Join-Path $testRoot 'Application'
$dataDir = Join-Path $testRoot 'UserData'
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$sentinel = Join-Path $dataDir 'race-data.txt'
'keep these test race results' | Set-Content -LiteralPath $sentinel
$dataHash = (Get-FileHash -LiteralPath $sentinel).Hash
function Run-Setup([string]$File, [string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(120000)) {
            $process.Kill($true)
            throw 'The test installer timed out; inspect the retained logs.'
        }
        return $process.ExitCode
    } finally { $process.Dispose() }
}
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
$common = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=$installDir", '/TASKS=')
$marker = [Threading.Mutex]::new($false, 'Local\YATSS.Desktop.Running')
try {
    $code = Run-Setup $Installer ($common + "/LOG=$(Join-Path $testRoot 'blocked-install.log')")
    Check ($code -ne 0 -and -not (Test-Path $key)) 'Setup did not refuse the running-app marker.'
} finally { $marker.Dispose() }
$code = Run-Setup $Installer ($common + "/LOG=$(Join-Path $testRoot 'install.log')")
Check ($code -eq 0) "Test installation failed with exit code $code. Logs: $testRoot"
Check (Test-Path $key) 'Missing test uninstall registration.'
$registration = Get-ItemProperty $key
Check ($registration.InstallLocation.TrimEnd('\') -eq $installDir) 'Unexpected registered installation directory.'
foreach ($file in @('YATSSWin.exe', 'YATSSWin.dll', 'YATSS.Protocol.dll', 'coreclr.dll',
    'Documentation\WINDOWS_INSTALLER.md', 'Linux\yatss-speech-helper.py')) {
    Check (Test-Path -LiteralPath (Join-Path $installDir $file)) "Missing installed file: $file"
}
Check (@(Get-ChildItem -LiteralPath (Join-Path $installDir 'Firmware') -Filter '*.yatssfw').Count -eq 4) 'Missing firmware packages.'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'YATSS Installer Test.lnk'
Check (Test-Path -LiteralPath $shortcut) 'Start-menu shortcut missing.'
$app = Join-Path $installDir 'YATSSWin.dll'
$appHash = (Get-FileHash -LiteralPath $app).Hash
$unowned = Join-Path $installDir 'not-owned-by-setup.txt'
'leave this file alone' | Set-Content -LiteralPath $unowned
$unownedHash = (Get-FileHash -LiteralPath $unowned).Hash
'damaged test app' | Set-Content -LiteralPath $app
$code = Run-Setup $Installer ($common + "/LOG=$(Join-Path $testRoot 'reinstall.log')")
Check ($code -eq 0 -and (Get-FileHash -LiteralPath $app).Hash -eq $appHash) 'Reinstall did not restore the application file.'
Check ((Get-FileHash -LiteralPath $sentinel).Hash -eq $dataHash) 'Test user data changed during installation.'
$uninstall = Join-Path $installDir 'unins000.exe'
$marker = [Threading.Mutex]::new($false, 'Local\YATSS.Desktop.Running')
try {
    $code = Run-Setup $uninstall @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$(Join-Path $testRoot 'blocked-uninstall.log')")
    Check ($code -ne 0 -and (Test-Path $key)) 'Uninstall did not refuse the running-app marker.'
} finally { $marker.Dispose() }
$code = Run-Setup $uninstall @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$(Join-Path $testRoot 'uninstall.log')")
Check ($code -eq 0) "Test uninstall failed with exit code $code."
Check (-not (Test-Path $key) -and -not (Test-Path -LiteralPath $shortcut)) 'Test registration/shortcut survived uninstall.'
Check (-not (Test-Path -LiteralPath $app)) 'Installer-owned app file survived uninstall.'
Check ((Test-Path -LiteralPath $unowned) -and (Get-FileHash -LiteralPath $unowned).Hash -eq $unownedHash -and
    (Get-FileHash -LiteralPath $sentinel).Hash -eq $dataHash) 'Uninstall removed/changed unowned files or test data.'
Write-Host "Installer running-app guards, install, reinstall, content and uninstall checks passed. Logs: $testRoot"
