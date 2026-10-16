# YATSS 0.25 Beta 3

**New: a self-contained Windows installer.** Install YATSS without separately
installing .NET, add a Start-menu entry and optional desktop shortcut, and remove
program files later without deleting race data. Portable ZIPs remain available,
including the smaller .NET 10 required package. Use ZIPs under Wine.

YATSS is an open-source alternative for slot-car timing and race control, not a
replacement for any particular organization's software or a claim of sanctioned
rules compliance. There are no production installations yet; this is a prerelease
for evaluation, demo races, controller development and careful bench testing.

## More Controllers Can Be Added

The current ESP32 and UNO Q list is not a permanent hardware restriction.
All supported boards use a shared controller core with board-specific pin,
interrupt and transport adapters. Additional microcontrollers can be supported
if their resources and electrical interfaces are suitable, followed by timing,
communication and fail-safe validation. Unsupported boards are not plug-and-play.
Open a GitHub issue with your board model and requirements if hardware choice
is keeping you from exploring YATSS. See [shared controller core](SHARED_CONTROLLER.md).

## Changes Since 0.20 Beta 2

- Optional per-user Windows setup, bundled documentation, stable installation
  identity, data-preserving uninstall and a running-app guard. Setup never closes
  a race automatically, installs drivers or flashes a controller.
- Combined Distance Racing: qualifying distance carried into race totals,
  hundredths-of-a-lap approvals, independent groups and separate Live Standings
  with projected totals. Working rules remain subject to venue-owner feedback.
- Independent Groups scheduling for ordinary fastest-lap-qualified heat races,
  including group-local initial lane choices and explicit group starts.
- Heat durations entered in seconds, from 1 second through 24 hours; new heat
  setups default to 60-second intermissions while retaining saved choices.
- Transactional active-event recovery, retained scoring and remaining time,
  restart-safe report exports, and stopped current-heat rollback/rerun with audit
  records. Previously completed heats and qualifying credit are preserved.
- Race Archives browser, report reconstruction into separate copies, and
  deliberate archive deletion without replacing an active race.
- Stronger controller-loss handling and protocol-5 timing-session identity.
  Unknown sessions interrupt racing; retired-session traffic is rejected;
  reconnect verifies identity, heartbeat and power off before director resume.
- Shared C++ controller implementation for ESP32 and UNO Q, preserving ISR
  timestamps and board-specific adapters. Local UNO Q updates from YATSS under
  Wine through an authenticated Linux helper; optional startup supervision.
- Isolated TCP controller simulator and real-app fault/recovery integration
  tests. Those tests found and fixed a qualifying elapsed-time underflow when
  the last confirmed heartbeat slightly preceded the estimated start.
- More reliable bounded background diagnostic logging and rollover, corrected
  downward lap-total corrections, and explicit Copy Selection / Copy All / Open
  Original controls in offline Help.

## Downloads

| Asset | Use |
| --- | --- |
| `YATSS-v0.25.0-beta.3-win-x64-setup.exe` | Recommended self-contained native Windows x64 installer |
| `YATSS-v0.25.0-beta.3-win-arm64-setup.exe` | Experimental self-contained native Windows ARM64 installer |
| `YATSS-win-x64-v0.25.0-beta.3.zip` | Self-contained portable x64 app, Windows or experimental x64 Wine |
| `YATSS-win-x64-requires-dotnet10-v0.25.0-beta.3.zip` | Smaller portable x64 app; requires x64 .NET 10 Desktop Runtime |
| `YATSS-win-arm64-v0.25.0-beta.3-experimental.zip` | Self-contained portable ARM64 app, including experimental ARM64 Wine |
| `YATSS-UNOQ-AppLab-v0.25.0-beta.3.zip` | Experimental UNO Q controller source for Arduino App Lab |

Verify files against `YATSS-v0.25.0-beta.3-SHA256.txt`. Checksums detect corruption,
not publisher identity. The app and installers are unsigned and may trigger
Windows reputation warnings. Installers require Windows 10 build 19045 or newer;
see [installation guide](WINDOWS_INSTALLER.md) for details and build instructions.
Select `Mode > Demo Race...` to try YATSS without controller hardware.

## Controller and Track-Power Requirements

This app requires session-aware protocol-5 firmware for physical timing.
The included controller firmware is `0.20.0-beta.2-session.1`; its version is
independent of the app's 0.25 release number. Matching ESP32 firmware packages
and UNO Q source are bundled. The optional UNO Q update helper must also match.
Official flashing tools are reused or downloaded after explicit operator
approval, not run by the Windows installer.

The documented relay arrangement is now **normally open 30-87**, with active-high
run outputs and external gate pulldowns. Review [relay wiring](RELAY_WIRING.md)
before applying track power. The earlier beta's normally closed wiring is not
the current design. Physical sensor, relay, watchdog and control-power-loss
validation remains required; software tests cannot certify an electrical system.

## Verification and Remaining Limits

- Native Windows regression: thirteen suites, including real-app simulated TCP
  faults; 6,722 shared C++ controller checks; nine mocked updater-helper tests.
- x64 installer smoke checks cover install/reinstall/uninstall, running-app
  refusal, restored application files, registration/shortcuts and preserved
  test data/unowned files. These are not complete interactive installer acceptance.
- A local UNO Q update under ARM64 Wine was user-verified with power held off
  and heartbeats returning. Failure/interruption and supervisor variants still
  require validation. Remote Windows-to-UNO-Q updates are not implemented.
- Earlier packaged suites passed on ARM64 Wine 10, but the expanded current
  suites and latest Help clipboard integration still need a matching Wine run.
- Native Windows ARM64 installer operation remains unverified. Wine/UNO Q
  operation remains experimental; use the matching portable ZIP.
- Full eight-lane physical timing, relay and production-harness validation,
  sustained real-sensor runs, crash/power-loss acceptance and venue-owner review
  remain pending. No production installations or sanctioned compliance claimed.

See [TODO](../TODO.md), [software validation](SOFTWARE_VALIDATION.md) and
[publish smoke test](PUBLISH_SMOKE_TEST.md) for the remaining checks.
