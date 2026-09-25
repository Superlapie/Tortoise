#!/usr/bin/env bash
# Enable RDP on the base image via Startup script + optional offline registry keys.
set -euo pipefail

LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
BASE="${1:-${TORTOISE_QEMU_BASE_IMAGE:-${LAB_ROOT}/base/windows-base.qcow2}}"
NBD="/dev/nbd0"
MNT_P2="/tmp/tortoise-qemu-p2"
STARTUP="${MNT_P2}/ProgramData/Microsoft/Windows/Start Menu/Programs/StartUp"
MARKER="${MNT_P2}/Windows/TortoiseLabRdpReady.tag"

cleanup() {
  sudo umount "${MNT_P2}" 2>/dev/null || true
  sudo qemu-nbd --disconnect "${NBD}" 2>/dev/null || true
}
trap cleanup EXIT

[[ -f "${BASE}" ]] || { echo "Missing base: ${BASE}" >&2; exit 1; }
pgrep -f 'qemu-system-x86_64' >/dev/null && {
  echo "Refusing inject while QEMU is running" >&2
  exit 1
}

sudo modprobe nbd max_part=8
sudo qemu-nbd --disconnect "${NBD}" 2>/dev/null || true
sudo qemu-nbd --connect="${NBD}" "${BASE}"
sleep 2
sudo ntfsfix -d "${NBD}p2" >/dev/null
sudo mkdir -p "${MNT_P2}"
if ! sudo mount -t ntfs-3g -o rw,remove_hiberfile "${NBD}p2" "${MNT_P2}"; then
  sudo ntfsfix -d "${NBD}p2" >/dev/null
  sudo mount -t ntfs-3g -o rw,remove_hiberfile,force "${NBD}p2" "${MNT_P2}" || {
    echo "MOUNT_FAIL partition=${NBD}p2" >&2
    exit 1
  }
fi
sudo mkdir -p "${STARTUP}"
if [[ ! -d "${MNT_P2}/Windows" ]]; then
  echo "MOUNT_SANITY_FAIL missing ${MNT_P2}/Windows" >&2
  exit 1
fi

sudo tee "${STARTUP}/tortoise-enable-rdp.cmd" >/dev/null <<'EOF'
@echo off
echo === startup bootstrap %DATE% %TIME% ===>> C:\Windows\Temp\tortoise-rdp.log
C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Windows\Temp\tortoise-enable-rdp.ps1 >> C:\Windows\Temp\tortoise-rdp.log 2>&1
EOF

sync
echo "RDP_STARTUP_INJECTED path=${STARTUP}/tortoise-enable-rdp.cmd"
