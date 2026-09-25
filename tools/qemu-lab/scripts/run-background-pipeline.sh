#!/usr/bin/env bash
# Background-safe pipeline: install, wait for RDP on install VM, seal base, prove overlay.
set -euo pipefail

LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
SCRIPTS="${LAB_ROOT}/scripts"
LOG="${LAB_ROOT}/logs/background-pipeline.log"
BASE="${LAB_ROOT}/base/windows-base.qcow2"
MIN_BASE_BYTES="${TORTOISE_QEMU_MIN_BASE_BYTES:-10000000000}"
RDP_PORT="${TORTOISE_QEMU_RDP_PORT:-53389}"
INSTALL_TIMEOUT_SEC="${TORTOISE_QEMU_INSTALL_TIMEOUT_SEC:-10800}"

exec >>"${LOG}" 2>&1
echo "=== PIPELINE $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="

export TORTOISE_QEMU_LAB_ROOT="${LAB_ROOT}"
export TORTOISE_QEMU_GUEST_CPUS="${TORTOISE_QEMU_GUEST_CPUS:-4}"
export TORTOISE_QEMU_GUEST_RAM_MB="${TORTOISE_QEMU_GUEST_RAM_MB:-4096}"
export TORTOISE_QEMU_BASE_IMAGE="${BASE}"
export TORTOISE_QEMU_MIN_BASE_BYTES="${MIN_BASE_BYTES}"

pkill -f 'qemu-system-x86_64' 2>/dev/null || true
sleep 3

export TORTOISE_QEMU_FORCE_BASE_REINSTALL=1
"${SCRIPTS}/install-windows-base.sh"

rdp_ready() {
  python3 - <<PY
import socket
s = socket.socket()
s.settimeout(3)
try:
    s.connect(("127.0.0.1", int("${RDP_PORT}")))
    data = s.recv(4)
    print("1" if data.startswith(b"\x03\x00") else "0")
except Exception:
    print("0")
finally:
    s.close()
PY
}

deadline=$((SECONDS + INSTALL_TIMEOUT_SEC))
enter_sent=0
last_log=0
while (( SECONDS < deadline )); do
  sz=$(stat -c%s "${BASE}" 2>/dev/null || echo 0)
  q=$(pgrep -c -f 'qemu-system-x86_64' || echo 0)

  if (( SECONDS - last_log >= 120 )); then
    echo "$(date -u +%H:%M:%S) base_bytes=${sz} qemu=${q} rdp=$(rdp_ready)"
    last_log=${SECONDS}
  fi

  if [[ "${sz}" -ge "${MIN_BASE_BYTES}" && "$(rdp_ready)" == "1" ]]; then
    echo "GUEST_REACHABLE rdp=127.0.0.1:${RDP_PORT} bytes=${sz}"
    break
  fi

  if [[ "${q}" == "0" ]]; then
    if [[ "${sz}" -lt 2000000000 ]]; then
      echo "INSTALL_FAILED qemu_exited bytes=${sz}"
      exit 1
    fi
    if [[ "${sz}" -ge "${MIN_BASE_BYTES}" ]] && "${SCRIPTS}/verify-base-bootable.sh" "${BASE}"; then
      echo "BASE_BOOTABLE qemu_exited bytes=${sz}"
      break
    fi
    echo "INSTALL_FAILED qemu_exited_before_ready bytes=${sz}"
    exit 1
  fi

  if [[ "${sz}" -ge "${MIN_BASE_BYTES}" && enter_sent -lt 6 && $((SECONDS)) -gt 900 ]]; then
    "${SCRIPTS}/qemu-monitor-cmd.sh" sendkey ret >/dev/null 2>&1 || true
    enter_sent=$((enter_sent + 1))
  fi

  sleep 20
done

if [[ "$(rdp_ready)" != "1" ]] && ! "${SCRIPTS}/verify-base-bootable.sh" "${BASE}"; then
  echo "BASE_INSTALL_NOT_READY" >&2
  exit 1
fi

"${SCRIPTS}/complete-base-install.sh" 300
"${SCRIPTS}/prove-overlay-cycle.sh"
echo "=== PIPELINE_OK $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
