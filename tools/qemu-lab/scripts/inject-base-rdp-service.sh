#!/usr/bin/env bash
# Register TortoiseRdpBootstrap as an auto-start Windows service on the base image.
set -euo pipefail

LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
BASE="${1:-${TORTOISE_QEMU_BASE_IMAGE:-${LAB_ROOT}/base/windows-base.qcow2}}"
NBD="/dev/nbd0"
MNT_P2="/tmp/tortoise-qemu-p2"
SYSTEM_HIVE="${MNT_P2}/Windows/System32/config/SYSTEM"

cleanup() {
  rm -f /tmp/tortoise-rdp-service.hive
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
sudo mount -t ntfs-3g -o rw,remove_hiberfile,force "${NBD}p2" "${MNT_P2}"

TMP_HIVE="/tmp/tortoise-rdp-service.hive"
cp "${SYSTEM_HIVE}" "${TMP_HIVE}"
chmod u+w "${TMP_HIVE}"

hivexsh -w "${TMP_HIVE}" <<'EOF' || true
cd \ControlSet001\Services
add TortoiseRdpBootstrap
commit
quit
EOF

hivexsh -w "${TMP_HIVE}" <<'EOF'
cd \ControlSet001\Services\TortoiseRdpBootstrap
setval 5
Type
dword:16
Start
dword:2
ErrorControl
dword:1
ImagePath
string:C:\Windows\System32\cmd.exe /c powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Windows\Temp\tortoise-enable-rdp.ps1
ObjectName
string:LocalSystem
commit
quit
EOF

sudo cp "${TMP_HIVE}" "${SYSTEM_HIVE}"
sync
echo "RDP_SERVICE_INJECTED service=TortoiseRdpBootstrap"
