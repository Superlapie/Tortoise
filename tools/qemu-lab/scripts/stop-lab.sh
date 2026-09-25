#!/usr/bin/env bash
# Stop only the Tortoise lab QEMU instance identified by LAB_ROOT/tmp/qemu.pid
set -euo pipefail

LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
PID_FILE="${LAB_ROOT}/tmp/qemu.pid"
CURRENT_RUN="${LAB_ROOT}/artifacts/current-run.json"

if [[ ! -f "${PID_FILE}" ]]; then
  echo "LAB_ALREADY_STOPPED"
  exit 0
fi

PID="$(cat "${PID_FILE}")"
if [[ ! -d "/proc/${PID}" ]]; then
  rm -f "${PID_FILE}" "${CURRENT_RUN}"
  echo "LAB_ALREADY_STOPPED stale_pid=${PID}"
  exit 0
fi

if ! tr '\0' ' ' < "/proc/${PID}/cmdline" | grep -q qemu-system; then
  echo "PID ${PID} is not a QEMU process owned by this lab; refusing." >&2
  exit 1
fi

# Graceful shutdown via ACPI (best effort), then bounded SIGTERM.
if command -v qemu-monitor-command >/dev/null 2>&1; then
  qemu-monitor-command --pid "${PID}" system_powerdown >/dev/null 2>&1 || true
  for _ in $(seq 1 30); do
    [[ -d "/proc/${PID}" ]] || break
    sleep 1
  done
fi

if [[ -d "/proc/${PID}" ]]; then
  kill -TERM "${PID}" || true
  for _ in $(seq 1 15); do
    [[ -d "/proc/${PID}" ]] || break
    sleep 1
  done
fi

if [[ -d "/proc/${PID}" ]]; then
  kill -KILL "${PID}" || true
fi

rm -f "${PID_FILE}" "${CURRENT_RUN}"
echo "LAB_STOPPED pid=${PID}"
