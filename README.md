# YATSS

YATSS is a slot-car lap timing and scoring system with two parts:

- `YATSSWin` is the Windows WinForms race-control app.
- `YATSSMC` is the ESP32-C6-DevKitC-1 and Arduino Nano ESP32 sensor and
  track-power controller sketch.

The microcontroller timestamps debounced sensor edges and reports them over
serial. The Windows app owns lap counting, heat-race flow, qualifying, reports,
logging, filtering, and track-power commands.

## Repository Layout

```text
YATSSWin/
  YATSS.sln
  YATSS/          Windows app project
  YATSS.Tests/    lightweight test runner

YATSSMC/
  YATSSMC.ino     ESP32-C6 and Arduino Nano ESP32 sketch

docs/
  SERIAL_PROTOCOL.md
  TROUBLESHOOTING.md
```

## Build And Test

From the repository root:

```powershell
dotnet build YATSSWin\YATSS.sln
dotnet run --project YATSSWin\YATSS.Tests\YATSS.Tests.csproj
arduino-cli compile --fqbn arduino:esp32:nano_nora YATSSMC
```

## Documentation

- [Windows app](YATSSWin/README.md)
- [Microcontroller sketch](YATSSMC/README.md)
- [Serial protocol](docs/SERIAL_PROTOCOL.md)
- [Race reports and data exports](docs/RACE_DATA_EXPORT.md)
- [Database backup and restore](docs/DATABASE_BACKUP.md)
- [Windows publish smoke test](docs/PUBLISH_SMOKE_TEST.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)

## License

YATSS is licensed under the [MIT License](LICENSE).
