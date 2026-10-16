# Windows Installer

YATSS offers an optional self-contained Windows installer alongside portable
ZIP downloads. No separate .NET installation or runtime download is required.
The installer is for native Windows; keep using the matching ZIP under Wine.

## Install, Update and Remove

- Choose the x64 setup for Intel/AMD Windows, or ARM64 setup for Windows on Arm.
  The x64 installer refuses Arm systems so the native package is used.
- Windows 10 22H2 (build 19045) or newer is required by this installer; Windows
  11 is supported. This minimum does not imply Microsoft still supports every
  Windows edition or that every supported OS has been tested with YATSS.
- Run setup as your ordinary desktop user. The default application directory is
  `%LOCALAPPDATA%\Programs\YATSS`; administrator access is not requested.
- A Start-menu shortcut is added. A desktop shortcut is optional and unchecked
  initially. Launching YATSS at the end is also optional and unchecked.
- Close YATSS normally before installing an update or uninstalling. Setup checks
  a running-app marker and does not automatically stop a race, close YATSS,
  restart it, or restart Windows. A silent operation fails if YATSS is running.
- Run a newer installer to update the same per-user installation. Stable app
  identity and previous folder/task choices are retained. This first version
  does not prevent installing an older build: check its version before proceeding.
- Remove YATSS through Windows Installed Apps. Uninstall removes installer-owned
  program files and shortcuts, not race data. Unknown files added to the app
  folder are left alone. There is deliberately no delete-user-data option.

Settings, journals and diagnostic logs remain in `%LOCALAPPDATA%\YATSS`.
Default reports remain in `Documents\YATSS Race Reports`, and backups in
`Documents\YATSS Backups` (including any configured external backup location).
The installer neither creates nor removes those data directories. Do not select
a race-data directory as the application installation folder.

Drivers, controller flashing, fonts, Piper voices and Wine/Linux service setup
are separate deliberate steps. Bundled firmware, helpers, UNO Q source and
offline documentation are copied with the application but never executed by
setup. There is no automatic updater or background service.

Self-contained distribution means .NET security fixes require rebuilding and
redistributing YATSS with a serviced runtime. The installer does not install a
shared .NET runtime. Unsigned developer builds may produce Windows reputation
or publisher warnings; a checksum detects corruption, not publisher identity.
No signing certificate or public release is created by the build script.

## Build

Install the .NET 10 SDK and [Inno Setup](https://jrsoftware.org/isdl.php) on the
Windows build machine. The implementation is tested with Inno Setup 7.1.0.
For a per-user compiler installation:

```powershell
winget install --id JRSoftware.InnoSetup.7 -e -s winget --scope user
```

From the repository root:

```powershell
./tools/Build-Installer.ps1
./tools/Build-Installer.ps1 -Runtime win-arm64
```

Or build both with `-Runtime win-x64,win-arm64`. Use `-Compiler` with an explicit
`ISCC.exe` path if it is installed elsewhere. The script publishes into a unique
fresh directory, verifies bundled content/runtime files, includes the MIT
license, and compiles `installer/YATSS.iss`. Existing firmware package/version
and shared-source checks still run. It does not compile or flash firmware.

Installers and SHA-256 sidecars go to `artifacts/installers`. Names and Installed
Apps version include Git build identity, including `dirty` for local changes.
Use `-OutputDirectory` for another folder; existing named output is not
overwritten. Fresh publish folders and a `build.json` manifest remain under
`artifacts/installer-builds`. Nothing is committed or uploaded automatically.

Inno's [license](https://jrsoftware.org/files/is/license.txt) permits use subject
to its conditions; its developers separately request commercial users purchase
a license. Preserve Inno notices. YATSS's own MIT license remains unchanged.

## Installer Smoke Test

Use a separate test-only setup, never the ordinary installer, for automated
install/update/remove checks on the matching native Windows architecture:

```powershell
./tools/Build-Installer.ps1 -TestInstaller -OutputDirectory artifacts/installer-smoke
./tools/Test-Installer.ps1 -Installer <absolute-path-to-the-generated-test.exe>
```

The test-only setup uses a different app identity, name, shortcut and uninstall
registration. It must not be distributed as a YATSS release. The smoke test
refuses an existing test installation, keeps its files/logs in a unique artifact
directory, verifies running-app blocking, performs a silent install and a second
install over it, checks installed file hashes, and uninstalls. It verifies that
test data outside the app and an unowned file inside the app survive removal.
It does not launch the race app, open user databases, or connect to controllers.

Before a public release, also verify the interactive wizard, optional desktop
shortcut, installed-app startup/Help/firmware discovery, upgrade with a newer
version, uninstall, and the matching ARM64 build on native Windows on Arm.
Wine testing remains on ZIPs. Code signing and enterprise MSI deployment are
not implemented by this first installer.
