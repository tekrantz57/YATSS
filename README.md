# MKTS

MKTS is a slot-car lap timing and scoring system with two parts:

- `MKTSWin` is the Windows WinForms race-control app.
- `MKTSMC` is the Arduino Nano ESP32 sensor and track-power controller sketch.

The microcontroller timestamps debounced sensor edges and reports them over
serial. The Windows app owns lap counting, heat-race flow, qualifying, reports,
logging, filtering, and track-power commands.

## Repository Layout

```text
MKTSWin/
  MKTS.sln
  MKTS/          Windows app project
  MKTS.Tests/    lightweight test runner

MKTSMC/
  MKTSMC.ino     Arduino Nano ESP32 sketch

docs/
  SERIAL_PROTOCOL.md
  TROUBLESHOOTING.md
```

## Build And Test

From the repository root:

```powershell
dotnet build MKTSWin\MKTS.sln
dotnet run --project MKTSWin\MKTS.Tests\MKTS.Tests.csproj
arduino-cli compile --fqbn arduino:esp32:nano_nora MKTSMC
```

## Documentation

- [Windows app](MKTSWin/README.md)
- [Microcontroller sketch](MKTSMC/README.md)
- [Serial protocol](docs/SERIAL_PROTOCOL.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)

## License

MKTS is licensed under the [MIT License](LICENSE).
