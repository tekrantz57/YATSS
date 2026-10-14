param([string]$ResultsDirectory = (Join-Path $PSScriptRoot 'Results'),
      [ValidateRange(30,3600)][int]$SuiteTimeoutSeconds = 600)
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'YATSS.Tests.exe'
if (-not (Test-Path -LiteralPath $runner)) { throw 'Extract the entire validation ZIP before running this script.' }
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$log = Join-Path $ResultsDirectory ("launcher-{0}-{1}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss'), [Guid]::NewGuid().ToString('N').Substring(0,8))
& $runner --validation ([IO.Path]::GetFullPath($ResultsDirectory)) $SuiteTimeoutSeconds 2>&1 | Tee-Object -FilePath $log
exit $LASTEXITCODE
