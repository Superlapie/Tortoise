#!/usr/bin/env bash
set -euo pipefail
LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
RDP_PORT="${TORTOISE_QEMU_RDP_PORT:-53389}"
TIMEOUT_SEC="${1:-5400}"
INSTALL_PID="${LAB_ROOT}/tmp/windows-install.pid"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
rdp_ready() {
  python3 "${SCRIPT_DIR}/rdp-probe.py" "127.0.0.1" "${RDP_PORT}" >/dev/null 2>&1
}

if [[ -f "${INSTALL_PID}" ]]; then
  install_pid="$(cat "${INSTALL_PID}")"
  if ! kill -0 "${install_pid}" 2>/dev/null; then
    rm -f "${INSTALL_PID}"
  fi
fi

min_base="${TORTOISE_QEMU_MIN_BASE_BYTES:-0}"
base_path="${TORTOISE_QEMU_BASE_IMAGE:-${LAB_ROOT}/base/windows-base.qcow2}"

deadline=$((SECONDS + TIMEOUT_SEC))
while (( SECONDS < deadline )); do
  if [[ "${min_base}" != "0" && -f "${base_path}" ]]; then
    base_sz="$(stat -c%s "${base_path}")"
    if [[ "${base_sz}" -lt "${min_base}" ]]; then
      sleep 20
      continue
    fi
  fi
  if rdp_ready; then
    echo "GUEST_REACHABLE rdp=127.0.0.1:${RDP_PORT}"
    exit 0
  fi
  if [[ -f "${INSTALL_PID}" ]]; then
    pid="$(cat "${INSTALL_PID}")"
    if ! kill -0 "${pid}" 2>/dev/null; then
      rm -f "${INSTALL_PID}"
    fi
  fi
  sleep 20
done
echo "GUEST_REACHABILITY_TIMEOUT seconds=${TIMEOUT_SEC}" >&2
exit 1
