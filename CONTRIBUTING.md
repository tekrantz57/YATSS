# Contributing to YATSS

YATSS is currently a prerelease project with no production track installations.
Questions, reproducible bug reports, hardware observations, and focused pull
requests are welcome through GitHub Issues and pull requests.

Before opening an issue, include:

- YATSS version or commit.
- Windows or Wine version and CPU architecture.
- Controller board and sketch version.
- Steps to reproduce the behavior.
- Relevant serial-log lines with personal paths or names removed.

Code changes should preserve the existing architecture: the controller reports
timestamped edges and owns immediate fail-safe actions, while the Windows app
owns race state, lap scoring, reports, and operator workflow. Run these checks
before submitting a pull request:

```powershell
dotnet build YATSSWin\YATSS.sln -c Release
dotnet run --project YATSSWin\YATSS.Tests\YATSS.Tests.csproj -c Release
arduino-cli compile --fqbn arduino:esp32:nano_nora YATSSMC
arduino-cli compile --fqbn esp32:esp32:esp32c6 YATSSMC
```

Do not include race databases, serial logs, generated reports, credentials, or
local machine configuration in issues or commits.
