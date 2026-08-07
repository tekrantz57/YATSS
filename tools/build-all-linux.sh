#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source_checkout="${1:-}"

if [[ -n "$source_checkout" ]]; then
  bash "$script_dir/build-unoq-app-linux.sh" "$source_checkout"
  bash "$script_dir/publish-yatss-arm64-linux.sh" "$source_checkout"
else
  bash "$script_dir/build-unoq-app-linux.sh"
  bash "$script_dir/publish-yatss-arm64-linux.sh"
fi
