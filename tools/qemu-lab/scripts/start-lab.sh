#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
BASE_IMAGE="${TORTOISE_QEMU_BASE_IMAGE:-${LAB_ROOT}/base/windows-base.qcow2}"
OVERLAY="${TORTOISE_QEMU_OVERLAY_IMAGE:-}"
RUN_ID="${1:-run-$(date -u +%Y%m%dT%H%M%SZ)}"
GUEST_RAM_MB="${TORTOISE_QEMU_GUEST_RAM_MB:-4096}"
GUEST_CPUS="${TORTOISE_QEMU_GUEST_CPUS:-2}"
RDP_PORT="${TORTOISE_QEMU_RDP_PORT:-53389}"
QEMU_BIN="${TORTOISE_QEMU_BIN:-qemu-system-x86_64}"

export TORTOISE_QEMU_LAB_ROOT="${LAB_ROOT}"
export TORTOISE_QEMU_MAX_LAB_DIR_KB="${TORTOISE_QEMU_MAX_LAB_DIR_KB:-167772160}"

run_kvm() {
  if [[ -r /dev/kvm && -w /dev/kvm ]]; then
    "$@"
  else
    sg kvm -c "$(printf '%q ' "$@")"
  fi
}

"${SCRIPT_DIR}/check-host-safety.sh"

[[ -f "${BASE_IMAGE}" ]] || { echo "Base image missing: ${BASE_IMAGE}" >&2; exit 1; }
command -v qemu-img >/dev/null || { echo "qemu-img not installed" >&2; exit 1; }
command -v "${QEMU_BIN}" >/dev/null || { echo "QEMU not installed: ${QEMU_BIN}" >&2; exit 1; }

mkdir -p "${LAB_ROOT}/overlays" "${LAB_ROOT}/logs" "${LAB_ROOT}/artifacts" "${LAB_ROOT}/tmp"

if [[ -z "${OVERLAY}" ]]; then
  OVERLAY="${LAB_ROOT}/overlays/${RUN_ID}.qcow2"
fi

if [[ ! -f "${OVERLAY}" ]]; then
  qemu-img create -f qcow2 -F qcow2 -b "${BASE_IMAGE}" "${OVERLAY}"
fi

LOG="${LAB_ROOT}/logs/${RUN_ID}.log"
PID_FILE="${LAB_ROOT}/tmp/qemu.pid"
CURRENT_RUN="${LAB_ROOT}/artifacts/current-run.json"

export TORTOISE_QEMU_BASE_IMAGE="${BASE_IMAGE}"
export TORTOISE_QEMU_OVERLAY_IMAGE="${OVERLAY}"
"${SCRIPT_DIR}/check-host-safety.sh"

CMD=(
  "${QEMU_BIN}"
  -machine pc,accel=kvm
  -m "${GUEST_RAM_MB}"
  -smp "${GUEST_CPUS}"
  -cpu host
  -drive "file=${OVERLAY},if=ide,format=qcow2"
  -netdev "user,id=n0,hostfwd=tcp:127.0.0.1:${RDP_PORT}-:3389"
  -device e1000e,netdev=n0
  -display none
  -daemonize
  -pidfile "${PID_FILE}"
)

printf '%q ' "${CMD[@]}" > "${LOG}.cmd"
echo >> "${LOG}.cmd"

run_kvm "${CMD[@]}" >> "${LOG}" 2>&1

cat > "${CURRENT_RUN}" <<EOF
{
  "runId": "${RUN_ID}",
  "pidFile": "${PID_FILE}",
  "overlayPath": "${OVERLAY}",
  "baseImagePath": "${BASE_IMAGE}",
  "guestRamMb": ${GUEST_RAM_MB},
  "guestCpus": ${GUEST_CPUS},
  "rdpLocalPort": ${RDP_PORT},
  "startedAtUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "commandLog": "${LOG}.cmd"
}
EOF

echo "LAB_STARTED run_id=${RUN_ID} rdp=127.0.0.1:${RDP_PORT} overlay=${OVERLAY}"
