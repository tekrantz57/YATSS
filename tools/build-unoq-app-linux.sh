#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $0 [YATSS source checkout]" >&2
}

find_repository() {
  local requested="${1:-}"
  local script_dir
  script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

  if [[ -n "$requested" ]]; then
    cd "$requested" && pwd
    return
  fi
  if [[ -f "$script_dir/../YATSSUnoQ/app.yaml" ]]; then
    cd "$script_dir/.." && pwd
    return
  fi
  if [[ -f "$PWD/YATSSUnoQ/app.yaml" ]]; then
    pwd
    return
  fi

  usage
  echo "Run this script from a YATSS checkout or pass the checkout path." >&2
  exit 2
}

arduino_cli="${ARDUINO_CLI:-arduino-cli}"
command -v "$arduino_cli" >/dev/null || {
  echo "Arduino CLI was not found. Set ARDUINO_CLI if it is not on PATH." >&2
  exit 1
}
command -v zip >/dev/null || { echo "The zip command is required." >&2; exit 1; }

repository_root="$(find_repository "${1:-}")"
app_root="$repository_root/YATSSUnoQ"
artifact_root="$repository_root/artifacts"
zip_path="$artifact_root/YATSS-UNOQ-AppLab.zip"

mkdir -p "$artifact_root"

"$arduino_cli" compile \
  --fqbn arduino:zephyr:unoq \
  "$app_root/sketch"

rm -f "$zip_path"
(
  cd "$app_root"
  zip -qr "$zip_path" . \
    -x '*/__pycache__/*' '*.pyc' '.cache/*' '*/.cache/*'
)

echo "Created $zip_path"
