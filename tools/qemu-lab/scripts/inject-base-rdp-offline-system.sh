#!/usr/bin/env bash
# Offline SYSTEM hive edits: enable RDP policy keys without touching multi-value service keys.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
BASE="${1:-${TORTOISE_QEMU_BASE_IMAGE:-${LAB_ROOT}/base/windows-base.qcow2}}"
NBD="/dev/nbd0"
MNT_P2="/tmp/tortoise-qemu-p2"
SYSTEM_HIVE="${MNT_P2}/Windows/System32/config/SYSTEM"

cleanup() {
  rm -f /tmp/tortoise-system-offline.hive
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

TMP_HIVE="/tmp/tortoise-system-offline.hive"
cp "${SYSTEM_HIVE}" "${TMP_HIVE}"
chmod u+w "${TMP_HIVE}"

hivexsh -w "${TMP_HIVE}" <<'EOF'
cd \ControlSet001\Control\Terminal Server
setval 1
fDenyTSConnections
dword:0
cd \ControlSet001\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile
setval 1
EnableFirewall
dword:0
cd \ControlSet001\Services\SharedAccess\Parameters\FirewallPolicy\PublicProfile
setval 1
EnableFirewall
dword:0
cd \ControlSet001\Services\SharedAccess\Parameters\FirewallPolicy\DomainProfile
setval 1
EnableFirewall
dword:0
commit
quit
EOF

python3 "${SCRIPT_DIR}/hive-patch-key.py" "${TMP_HIVE}" \
  '\ControlSet001\Control\Terminal Server\WinStations\RDP-Tcp' \
  UserAuthentication=0 SecurityLayer=1 MinEncryptionLevel=1

sudo cp "${TMP_HIVE}" "${SYSTEM_HIVE}"
sync
echo "RDP_OFFLINE_SYSTEM_INJECTED hive=${SYSTEM_HIVE}"
