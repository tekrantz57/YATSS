#!/usr/bin/env bash
set -euo pipefail

app_ref="${YATSS_UNOQ_APP:-$HOME/ArduinoApps/YATSSUnoQ}"
app_cli="${ARDUINO_APP_CLI:-arduino-app-cli}"
host="${YATSS_UNOQ_HOST:-127.0.0.1}"
port="${YATSS_UNOQ_PORT:-45991}"
start_timeout_seconds="${YATSS_UNOQ_START_TIMEOUT:-120}"
check_interval_seconds="${YATSS_UNOQ_CHECK_INTERVAL:-5}"
restart_backoff_seconds="${YATSS_UNOQ_RESTART_BACKOFF:-10}"

log() {
  printf '%s yatss-unoq-controller: %s\n' "$(date --iso-8601=seconds)" "$*" >&2
}

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    log "required command not found: $1"
    exit 127
  fi
}

validate_app_directory() {
  if [[ ! -d "$app_ref" ]]; then
    log "using App Lab reference '$app_ref'; source-directory validation is not available"
    return
  fi

  local app_yaml="$app_ref/app.yaml"
  local bridge="$app_ref/python/main.py"
  local sketch="$app_ref/sketch/sketch.ino"
  if [[ ! -f "$app_yaml" || ! -f "$bridge" || ! -f "$sketch" ]]; then
    log "refusing to start '$app_ref'; expected app.yaml, python/main.py, and sketch/sketch.ino"
    exit 2
  fi
  if ! grep -Eq '^name:[[:space:]]*YATSS UNO Q Controller[[:space:]]*$' "$app_yaml"; then
    log "refusing to start '$app_ref'; app.yaml does not identify the YATSS UNO Q Controller"
    exit 2
  fi
  if ! grep -Eq '^[[:space:]]*-[[:space:]]*45991[[:space:]]*$' "$app_yaml"; then
    log "refusing to start '$app_ref'; app.yaml does not expose TCP port 45991"
    exit 2
  fi
  if ! grep -Eq '^TCP_PORT[[:space:]]*=[[:space:]]*45991[[:space:]]*$' "$bridge"; then
    log "refusing to start '$app_ref'; python/main.py does not publish TCP port 45991"
    exit 2
  fi
}

tcp_listener_ready() {
  [[ -n "$(ss -H -ltn "sport = :$port")" ]]
}

wait_for_tcp() {
  local deadline=$((SECONDS + start_timeout_seconds))
  while (( SECONDS < deadline )); do
    if tcp_listener_ready; then
      return 0
    fi
    sleep 2
  done
  return 1
}

stop_app() {
  "$app_cli" app stop "$app_ref" >/dev/null 2>&1 || true
}

dump_app_logs() {
  timeout 10s "$app_cli" app logs "$app_ref" 2>&1 | tail -n 80 >&2 || true
}

start_app() {
  validate_app_directory
  stop_app
  log "starting App Lab app: $app_ref"
  "$app_cli" app start "$app_ref"
  if wait_for_tcp; then
    log "controller TCP port is available at $host:$port"
    return 0
  fi

  log "controller TCP port did not become available within ${start_timeout_seconds}s"
  dump_app_logs
  return 1
}

cleanup() {
  log "stopping App Lab app"
  stop_app
}

stop_service() {
  cleanup
  exit 0
}

require_command "$app_cli"
require_command ss
require_command timeout
trap cleanup EXIT
trap stop_service INT TERM

while true; do
  if ! start_app; then
    log "startup failed; retrying in ${restart_backoff_seconds}s"
    sleep "$restart_backoff_seconds"
    continue
  fi

  while tcp_listener_ready; do
    sleep "$check_interval_seconds"
  done

  log "controller TCP port disappeared; restarting App Lab app in ${restart_backoff_seconds}s"
  dump_app_logs
  sleep "$restart_backoff_seconds"
done
