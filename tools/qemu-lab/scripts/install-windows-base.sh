#!/usr/bin/env bash
# Install pristine Windows Server 2025 eval base image into qcow2 on ephemeral lab NVMe.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
ISO="${LAB_ROOT}/iso/windows-server-2025-eval.iso"
BASE="${LAB_ROOT}/base/windows-base.qcow2"
INSTALL_PID="${LAB_ROOT}/tmp/windows-install.pid"
INSTALL_LOG="${LAB_ROOT}/logs/windows-base-install.log"
SERIAL_LOG="${LAB_ROOT}/logs/windows-base-install-serial.log"
GUEST_RAM_MB="${TORTOISE_QEMU_GUEST_RAM_MB:-4096}"
GUEST_CPUS="${TORTOISE_QEMU_GUEST_CPUS:-2}"
RDP_PORT="${TORTOISE_QEMU_RDP_PORT:-53389}"
MONITOR_PORT="${TORTOISE_QEMU_MONITOR_PORT:-4444}"
AUTO_DIR="${LAB_ROOT}/tmp/autounattend"
AUTO_ISO="${LAB_ROOT}/tmp/autounattend.iso"
AUTO_FLOP="${LAB_ROOT}/tmp/autounattend.img"
MIN_COMPLETE_BASE_BYTES="${TORTOISE_QEMU_MIN_BASE_BYTES:-2000000000}"
BASE_FINGERPRINT="${LAB_ROOT}/config/base-image.sha256"
FORCE_REINSTALL="${TORTOISE_QEMU_FORCE_BASE_REINSTALL:-0}"

export TORTOISE_QEMU_LAB_ROOT="${LAB_ROOT}"
export TORTOISE_QEMU_MAX_LAB_DIR_KB="${TORTOISE_QEMU_MAX_LAB_DIR_KB:-167772160}"

"${SCRIPT_DIR}/check-storage-state.sh"
"${SCRIPT_DIR}/check-host-safety.sh"

if [[ -f "${BASE}" ]]; then
  base_sz="$(stat -c%s "${BASE}")"
  if [[ "${FORCE_REINSTALL}" == "1" ]]; then
    echo "Force reinstall requested; removing ${BASE} (${base_sz} bytes)"
    rm -f "${BASE}" "${BASE_FINGERPRINT}"
  elif [[ -f "${BASE_FINGERPRINT}" && "${base_sz}" -ge "${MIN_COMPLETE_BASE_BYTES}" ]]; then
    echo "BASE_ALREADY_SEALED path=${BASE} bytes=${base_sz} sha256=$(cat "${BASE_FINGERPRINT}")"
    exit 0
  elif [[ "${base_sz}" -ge "${MIN_COMPLETE_BASE_BYTES}" ]]; then
    echo "BASE_EXISTS_UNSEALED path=${BASE} bytes=${base_sz} (run complete-base-install.sh or set TORTOISE_QEMU_FORCE_BASE_REINSTALL=1)"
    exit 0
  else
    echo "Removing incomplete base image (${base_sz} bytes): ${BASE}"
    rm -f "${BASE}" "${BASE_FINGERPRINT}"
  fi
fi

[[ -f "${ISO}" ]] || { echo "Missing ISO: ${ISO}" >&2; exit 1; }

mkdir -p "${LAB_ROOT}/base" "${LAB_ROOT}/logs" "${LAB_ROOT}/tmp" "${LAB_ROOT}/artifacts"
chmod +x "${SCRIPT_DIR}/prepare-autounattend.sh"
mapfile -t auto_out < <("${SCRIPT_DIR}/prepare-autounattend.sh" "${AUTO_DIR}")
for line in "${auto_out[@]}"; do
  if [[ "${line}" == ADMIN_PASSWORD=* ]]; then
    ADMIN_PASSWORD="${line#ADMIN_PASSWORD=}"
  fi
done

[[ -n "${ADMIN_PASSWORD:-}" ]] || { echo "Failed to prepare autounattend" >&2; exit 1; }
[[ -f "${AUTO_FLOP}" ]] || { echo "Missing autounattend floppy: ${AUTO_FLOP}" >&2; exit 1; }

umask 077
cat > "${LAB_ROOT}/config/lab-credentials.json" <<EOF
{
  "adminUser": "Administrator",
  "adminPassword": "${ADMIN_PASSWORD}",
  "note": "Ephemeral lab only. Regenerated if storage is recreated."
}
EOF

cp "${AUTO_ISO}" "${LAB_ROOT}/config/autounattend.iso"
cp "${AUTO_FLOP}" "${LAB_ROOT}/config/autounattend.img"

qemu-img create -f qcow2 "${BASE}" 80G
: > "${SERIAL_LOG}"
: > "${INSTALL_LOG}"

DISPLAY_ARGS=(-display none)
if [[ "${TORTOISE_QEMU_INSTALL_VNC:-0}" == "1" ]]; then
  DISPLAY_ARGS=(-vga std -vnc "127.0.0.1:9")
fi

CMD=(qemu-system-x86_64 -machine pc,accel=kvm -cpu host -smp "${GUEST_CPUS}" -m "${GUEST_RAM_MB}"
  -drive "file=${BASE},if=ide,index=0,format=qcow2"
  -drive "file=${ISO},media=cdrom,if=ide,index=2,readonly=on"
  -drive "file=${AUTO_FLOP},if=floppy,format=raw,readonly=on"
  -netdev "user,id=n0,hostfwd=tcp:127.0.0.1:${RDP_PORT}-:3389"
  -device e1000,netdev=n0
  -monitor "tcp:127.0.0.1:${MONITOR_PORT},server,nowait"
  -boot once=d,menu=off,splash-time=0
  "${DISPLAY_ARGS[@]}"
  -serial "file:${SERIAL_LOG}")

printf '%q ' "${CMD[@]}" > "${INSTALL_LOG}.cmd"
echo >> "${INSTALL_LOG}.cmd"

nohup sg kvm -c "$(printf '%q ' "${CMD[@]}")" >> "${INSTALL_LOG}" 2>&1 &
echo $! > "${INSTALL_PID}"
sleep 3
if ! kill -0 "$(cat "${INSTALL_PID}")" 2>/dev/null; then
  echo "WINDOWS_INSTALL_FAILED" >&2
  tail -20 "${INSTALL_LOG}" >&2
  exit 1
fi

echo "WINDOWS_INSTALL_STARTED pid=$(cat "${INSTALL_PID}") log=${INSTALL_LOG} autounattend_floppy=${AUTO_FLOP} rdp_probe=127.0.0.1:${RDP_PORT}"
