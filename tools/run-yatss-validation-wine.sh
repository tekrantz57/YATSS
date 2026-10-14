#!/usr/bin/env bash
set -euo pipefail
package_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results_dir="${1:-$package_dir/Results}"
timeout_seconds="${2:-600}"
wine_command="${YATSS_VALIDATION_WINE_COMMAND:-wine}"
command -v "$wine_command" >/dev/null || { echo "Wine command not found: $wine_command" >&2; exit 2; }
[[ -f "$package_dir/YATSS.Tests.exe" ]] || { echo 'Extract the complete validation ZIP first.' >&2; exit 2; }
[[ "$timeout_seconds" =~ ^[0-9]+$ ]] && (( timeout_seconds >= 30 && timeout_seconds <= 3600 )) || {
  echo 'Suite timeout must be 30..3600 seconds.' >&2; exit 2;
}
mkdir -p "$results_dir"
results_dir="$(cd "$results_dir" && pwd)"
# Keep validation away from the user's application prefix. Never stop a global wineserver.
export WINEPREFIX="${YATSS_VALIDATION_WINEPREFIX:-$HOME/.local/share/YATSS-validation-wine}"
export YATSS_VALIDATION_WINE_VERSION="$("$wine_command" --version)"
winepath_command="${YATSS_VALIDATION_WINEPATH_COMMAND:-winepath}"
command -v "$winepath_command" >/dev/null || { echo "winepath command not found: $winepath_command" >&2; exit 2; }
results_windows="$("$winepath_command" -w "$results_dir")"
echo "Wine: $YATSS_VALIDATION_WINE_VERSION"
echo "Validation prefix: $WINEPREFIX"
log="$results_dir/launcher-$(date +%Y%m%d-%H%M%S)-$$.log"
{
  uname -srm
  echo "Wine: $YATSS_VALIDATION_WINE_VERSION"
  echo "Validation prefix: $WINEPREFIX"
  "$wine_command" "$package_dir/YATSS.Tests.exe" --validation "$results_windows" "$timeout_seconds"
} 2>&1 | tee "$log"
