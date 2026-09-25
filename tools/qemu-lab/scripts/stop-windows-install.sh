#!/usr/bin/env bash
set -euo pipefail

LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
PID_FILE="${LAB_ROOT}/tmp/windows-install.pid"

[[ -f "${PID_FILE}" ]] || { echo "WINDOWS_INSTALL_NOT_RUNNING"; exit 0; }

PID="$(cat "${PID_FILE}")"
if [[ -d "/proc/${PID}" ]]; then
  if tr '\0' ' ' < "/proc/${PID}/cmdline" | grep -q qemu-system; then
    kill -TERM "${PID}" || true
    for _ in $(seq 1 30); do
      [[ -d "/proc/${PID}" ]] || break
      sleep 2
    done
    [[ -d "/proc/${PID}" ]] && kill -KILL "${PID}" || true
  fi
fi

rm -f "${PID_FILE}"
echo "WINDOWS_INSTALL_STOPPED"
