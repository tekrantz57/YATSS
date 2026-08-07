# Building YATSS On Linux

YATSS remains a .NET 10 Windows Forms application, but the Windows ARM64 Wine
publish and Arduino UNO Q App Lab controller package can both be built from a
Linux source checkout. Three Bash scripts are available under `tools` and are
also copied into release output under `Linux/Build`.

## Prerequisites

Install these tools before building:

- Git and `zip`.
- The .NET 10 SDK for the Windows ARM64 application.
- Arduino CLI, the `arduino:zephyr` UNO Q core, and the libraries declared in
  `YATSSUnoQ/sketch/sketch.yaml` for the controller.

The repository must contain the packaged ESP32/Nano controller firmware under
`YATSSMC/dist`; the application publish deliberately fails when required
firmware packages are absent.

## Build Both Packages

From the root of a YATSS source checkout:

```bash
bash tools/build-all-linux.sh
```

This creates:

```text
artifacts/YATSS-UNOQ-AppLab.zip
artifacts/YATSS-win-arm64-linux-build.zip
```

The Windows ZIP is self-contained and targets `win-arm64` for Windows ARM64 or
experimental Wine-on-ARM64 use. The build enables Windows targeting explicitly
because the .NET SDK is running on Linux.

## Build Separately

Compile the UNO Q STM32 sketch and create its App Lab import ZIP:

```bash
bash tools/build-unoq-app-linux.sh
```

Publish and ZIP the Windows ARM64 application:

```bash
bash tools/publish-yatss-arm64-linux.sh
```

Set `ARDUINO_CLI` when Arduino CLI is not on `PATH`:

```bash
ARDUINO_CLI="$HOME/bin/arduino-cli" bash tools/build-unoq-app-linux.sh
```

## Running Scripts From A Published ZIP

A binary publish does not contain the complete source tree. Pass a Git checkout
path when using the copies under `Linux/Build`:

```bash
bash Linux/Build/build-all-linux.sh "$HOME/src/YATSS"
```

Using `bash` avoids relying on executable permission bits surviving a ZIP made
on Windows. The scripts write distributable output to the checkout's
`artifacts` directory, and the underlying build tools create their normal
`bin`, `obj`, and cache files. They do not install packages, alter system
configuration, flash the UNO Q, or modify tracked source files.
