#!/usr/bin/env bash
set -euo pipefail

LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
PID_FILE="${LAB_ROOT}/tmp/qemu.pid"
CURRENT_RUN="${LAB_ROOT}/artifacts/current-run.json"

if [[ ! -f "${PID_FILE}" ]]; then
  echo "LAB_STATUS=stopped"
  exit 0
fi

PID="$(cat "${PID_FILE}")"
if [[ -d "/proc/${PID}" ]] && tr '\0' ' ' < "/proc/${PID}/cmdline" | grep -q qemu-system; then
  echo "LAB_STATUS=running pid=${PID}"
  [[ -f "${CURRENT_RUN}" ]] && cat "${CURRENT_RUN}"
  exit 0
fi

echo "LAB_STATUS=stale pid_file=${PID_FILE}"
exit 1
