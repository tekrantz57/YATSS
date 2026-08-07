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
  if [[ -f "$script_dir/../YATSSWin/YATSS/YATSS.csproj" ]]; then
    cd "$script_dir/.." && pwd
    return
  fi
  if [[ -f "$PWD/YATSSWin/YATSS/YATSS.csproj" ]]; then
    pwd
    return
  fi

  usage
  echo "Run this script from a YATSS checkout or pass the checkout path." >&2
  exit 2
}

command -v dotnet >/dev/null || { echo "The .NET 10 SDK is required." >&2; exit 1; }
command -v zip >/dev/null || { echo "The zip command is required." >&2; exit 1; }

repository_root="$(find_repository "${1:-}")"
project="$repository_root/YATSSWin/YATSS/YATSS.csproj"
artifact_root="$repository_root/artifacts"
publish_dir="$artifact_root/YATSS-win-arm64-linux-build"
zip_path="$artifact_root/YATSS-win-arm64-linux-build.zip"

case "$publish_dir" in
  "$artifact_root"/*) ;;
  *) echo "Refusing to clean unexpected publish path: $publish_dir" >&2; exit 1 ;;
esac

mkdir -p "$artifact_root"
rm -rf "$publish_dir"
rm -f "$zip_path"

dotnet publish "$project" \
  --configuration Release \
  --runtime win-arm64 \
  --self-contained true \
  -p:EnableWindowsTargeting=true \
  --output "$publish_dir"

(
  cd "$publish_dir"
  zip -qr "$zip_path" .
)

echo "Created $zip_path"
