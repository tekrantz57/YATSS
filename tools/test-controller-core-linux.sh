#!/usr/bin/env bash
set -euo pipefail
root="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
bash "$root/tools/sync-controller-core-linux.sh" "$root" check
mkdir -p "$root/artifacts/controller-tests"
"${CXX:-g++}" -std=c++17 -Wall -Wextra -Werror -pedantic \
  "$root/tests/controller/main.cpp" -o "$root/artifacts/controller-tests/controller-tests"
"$root/artifacts/controller-tests/controller-tests"
