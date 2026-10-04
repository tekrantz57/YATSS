#!/usr/bin/env bash
set -euo pipefail
root="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
mode="${2:-sync}"
for folder in YATSSMC/src/YatssController YATSSUnoQ/sketch/src/YatssController; do
  for file in YatssController.h FirmwareVersion.h; do
    if [[ "$mode" == check ]]; then
      cmp "$root/Controller/$file" "$root/$folder/$file" || {
        echo "Shared controller copy is stale; run tools/sync-controller-core-linux.sh." >&2
        exit 1
      }
    else
      mkdir -p "$root/$folder"
      cp "$root/Controller/$file" "$root/$folder/$file"
    fi
  done
done
if [[ "$mode" == check ]]; then
  cmp "$root/Controller/FirmwareVersion.h" "$root/YATSSMC/FirmwareVersion.h"
else
  cp "$root/Controller/FirmwareVersion.h" "$root/YATSSMC/FirmwareVersion.h"
fi
echo "Shared controller sources are synchronized."
