[CmdletBinding()]
param([string]$OutputDirectory = '', [string]$Compiler = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $root 'YATSSWin\YATSS\YATSS.csproj'
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$tag = 'v' + [string]$project.Project.PropertyGroup.Version
$status = & git -C $root status --porcelain
if ($LASTEXITCODE -ne 0 -or $status) { throw 'Release packaging requires a clean source tree.' }
$actualTag = & git -C $root describe --exact-match --tags HEAD
if ($LASTEXITCODE -ne 0 -or $actualTag -ne $tag) { throw "HEAD must be tagged $tag before packaging." }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root "artifacts\releases\$tag" }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh release output directory; existing files are never overwritten.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$installers = @(& (Join-Path $PSScriptRoot 'Build-Installer.ps1') -Runtime win-x64,win-arm64 -OutputDirectory $output -Compiler $Compiler |
    ForEach-Object { if ($_.PSObject.Properties['Installer']) { $_ } else { Write-Host $_ } })
if ($installers.Count -ne 2) { throw 'Expected both installer build results.' }
$x64 = $installers | Where-Object Runtime -eq 'win-x64'
$arm64 = $installers | Where-Object Runtime -eq 'win-arm64'
Compress-Archive -Path (Join-Path $x64.PublishDirectory '*') -DestinationPath (Join-Path $output "YATSS-win-x64-$tag.zip")
Compress-Archive -Path (Join-Path $arm64.PublishDirectory '*') -DestinationPath (Join-Path $output "YATSS-win-arm64-$tag-experimental.zip")
$work = Join-Path $root ('artifacts\release-builds\' + [Guid]::NewGuid().ToString('N'))
$portable = Join-Path $work 'requires-dotnet10'
& dotnet publish $projectPath -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:PublishTrimmed=false -o $portable
if ($LASTEXITCODE -ne 0) { throw 'The .NET 10 required publish failed.' }
Copy-Item -LiteralPath (Join-Path $x64.PublishDirectory 'YATSS-LICENSE.txt') -Destination $portable
Copy-Item -LiteralPath (Join-Path $x64.PublishDirectory 'Licenses') -Destination $portable -Recurse
Compress-Archive -Path (Join-Path $portable '*') -DestinationPath (Join-Path $output "YATSS-win-x64-requires-dotnet10-$tag.zip")
& (Join-Path $PSScriptRoot 'Build-UnoQAppPackage.ps1') -OutputPath (Join-Path $output "YATSS-UNOQ-AppLab-$tag.zip")
if ($LASTEXITCODE -ne 0) { throw 'UNO Q source packaging failed.' }
$expected = @("YATSS-$tag-win-x64-setup.exe", "YATSS-$tag-win-arm64-setup.exe",
    "YATSS-win-x64-$tag.zip", "YATSS-win-arm64-$tag-experimental.zip",
    "YATSS-win-x64-requires-dotnet10-$tag.zip", "YATSS-UNOQ-AppLab-$tag.zip")
$hashes = foreach ($name in $expected) {
    $path = Join-Path $output $name
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing release asset: $name" }
    "$( (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash )  $name"
}
$hashes | Set-Content -LiteralPath (Join-Path $output "YATSS-$tag-SHA256.txt") -Encoding ascii
$runtimeConfig = Get-Content -LiteralPath (Join-Path $portable 'YATSSWin.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $runtimeConfig.runtimeOptions.PSObject.Properties['frameworks'] -or
    (Test-Path -LiteralPath (Join-Path $portable 'coreclr.dll'))) { throw 'Smaller ZIP must require a separately installed runtime.' }
$finalStatus = & git -C $root status --porcelain
if ($LASTEXITCODE -ne 0 -or $finalStatus) { throw 'Source tree changed while release assets were being built; do not publish them.' }
Write-Host "Release assets ready in $output. Nothing has been uploaded or released."
