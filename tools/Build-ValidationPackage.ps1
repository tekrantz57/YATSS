param([ValidateSet('win-x64','win-arm64')][string[]]$Runtime = @('win-x64','win-arm64'),
      [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
foreach ($rid in $Runtime) {
    $name = "YATSS-validation-$rid-$stamp-$([Guid]::NewGuid().ToString('N').Substring(0,8))"
    $folder = Join-Path $output $name
    & dotnet publish (Join-Path $root 'YATSSWin\YATSS.Tests\YATSS.Tests.csproj') -c Release -r $rid `
        --self-contained true -p:EnableWindowsTargeting=true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $folder
    if ($LASTEXITCODE -ne 0) { throw "Validation publish failed for $rid; no ZIP created." }
    foreach ($guide in @('QUICK_START.md','COMMISSIONING_CHECKLIST.md','INDEPENDENT_HEAT_GROUPS.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $folder "Documentation\$guide"))) { throw "Missing published guide $guide" }
    }
    $zip = "$folder.zip"
    Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $zip
    Write-Host "Created $zip"
}
