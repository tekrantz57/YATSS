[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')][string[]]$Runtime = @('win-x64'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\installers'),
    [string]$Compiler = '',
    [switch]$TestInstaller
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
if (-not $Compiler) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    $Compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $Compiler -or -not (Test-Path -LiteralPath $Compiler)) {
    throw 'Install Inno Setup 7 (winget install --id JRSoftware.InnoSetup.7 -e -s winget --scope user), or specify -Compiler with the path to ISCC.exe.'
}
$Compiler = (Resolve-Path -LiteralPath $Compiler).Path
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'YATSSWin\YATSS\YATSS.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$numericVersion = [string]$project.Project.PropertyGroup.FileVersion
$build = "v$version"
if (Test-Path -LiteralPath (Join-Path $root '.git')) {
    $description = & git -C $root describe --tags --always --long --dirty
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine source build identity.' }
    $build = $description.Trim()
}
if ($build -notmatch '^[a-zA-Z0-9._-]+$') { throw 'Unsafe build identity for installer filename.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$work = Join-Path $root ('artifacts\installer-builds\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $output, $work | Out-Null
$results = @()
foreach ($rid in $Runtime) {
    $publish = Join-Path $work $rid
    & dotnet publish (Join-Path $root 'YATSSWin\YATSS\YATSS.csproj') -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false -o $publish
    if ($LASTEXITCODE -ne 0) { throw "Self-contained publish failed for $rid; installer not built." }
    foreach ($required in @('YATSSWin.exe', 'YATSSWin.dll', 'YATSS.Protocol.dll', 'coreclr.dll',
        'hostfxr.dll', 'Documentation\QUICK_START.md', 'Documentation\WINDOWS_INSTALLER.md',
        'Linux\yatss-speech-helper.py', 'Linux\yatss-unoq-update-helper.py', 'Linux\UNOQ\app.yaml')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publish $required))) { throw "Missing published file: $required" }
    }
    if (@(Get-ChildItem -LiteralPath (Join-Path $publish 'Firmware') -Filter '*.yatssfw').Count -ne 4) {
        throw 'Expected exactly four bundled controller firmware packages.'
    }
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $publish 'YATSS-LICENSE.txt')
    $assets = Get-Content -LiteralPath (Join-Path $root 'YATSSWin\YATSS\obj\project.assets.json') -Raw | ConvertFrom-Json
    foreach ($dependency in $assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' }) {
        $packagePath = $assets.packageFolders.PSObject.Properties.Name | ForEach-Object {
            Join-Path $_ $dependency.Value.path
        } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (-not $packagePath) { throw "Dependency notices not found: $($dependency.Name)" }
        $destination = Join-Path $publish ('Licenses\NuGet\' + $dependency.Value.path)
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        Get-ChildItem -LiteralPath $packagePath -File | Where-Object {
            $_.Extension -eq '.nuspec' -or $_.Name -like 'LICENSE*' -or
            $_.Name -like 'NOTICE*' -or $_.Name -like 'THIRD-PARTY*'
        } | Copy-Item -Destination $destination
    }
    $downloads = $assets.project.frameworks.'net10.0-windows7.0'.downloadDependencies
    foreach ($packName in @("Microsoft.NETCore.App.Runtime.$rid", "Microsoft.WindowsDesktop.App.Runtime.$rid")) {
        $pack = $downloads | Where-Object name -eq $packName | Select-Object -First 1
        if (-not $pack) { throw "Could not resolve runtime notices for $packName." }
        $packVersion = $pack.version.Trim('[', ']').Split(',')[0].Trim()
        $packPath = $assets.packageFolders.PSObject.Properties.Name | ForEach-Object {
            Join-Path $_ ($packName.ToLowerInvariant() + '\' + $packVersion)
        } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (-not $packPath) { throw "Runtime package not found: $packName $packVersion" }
        $notices = Join-Path $publish ("Licenses\$packName")
        New-Item -ItemType Directory -Force -Path $notices | Out-Null
        $license = Get-ChildItem -LiteralPath $packPath -File | Where-Object Name -in @('LICENSE', 'LICENSE.TXT') | Select-Object -First 1
        if (-not $license) { throw "Missing runtime license: $packName" }
        Copy-Item -LiteralPath $license.FullName -Destination $notices
        $thirdParty = Join-Path $packPath 'THIRD-PARTY-NOTICES.TXT'
        if (Test-Path -LiteralPath $thirdParty) { Copy-Item -LiteralPath $thirdParty -Destination $notices }
    }
    $arch = $rid.Substring(4)
    $suffix = if ($TestInstaller) { '-test' } else { '' }
    $filename = "YATSS-$build-win-$arch-setup$suffix.exe"
    if (Test-Path -LiteralPath (Join-Path $output $filename)) { throw "Output already exists: $filename. Choose a fresh output directory." }
    $arguments = @('/Qp', "/DPublishDir=$publish", "/DOutputDir=$output", "/DBuildVersion=$build",
        "/DNumericVersion=$numericVersion", "/DRuntimeArch=$arch")
    if ($TestInstaller) { $arguments += '/DTestInstaller=1' }
    & $Compiler @arguments (Join-Path $root 'installer\YATSS.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed for $rid." }
    $file = Join-Path $output $filename
    if (-not (Test-Path -LiteralPath $file)) { throw 'Compiler did not produce the expected installer.' }
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    "$hash  $filename" | Set-Content -LiteralPath "$file.sha256" -Encoding ascii
    $results += [pscustomobject]@{ Runtime = $rid; Installer = $file; SHA256 = $hash; PublishDirectory = $publish; TestOnly = [bool]$TestInstaller }
}
$results | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $work 'build.json') -Encoding utf8
$results
