#!/usr/bin/env bash
# Copy generated autounattend.xml into the sealed Windows Panther folder on the base disk.
set -euo pipefail

LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
BASE="${1:-${TORTOISE_QEMU_BASE_IMAGE:-${LAB_ROOT}/base/windows-base.qcow2}}"
AUTO_XML="${2:-${LAB_ROOT}/tmp/autounattend/autounattend.xml}"
NBD="/dev/nbd0"
MNT_P2="/tmp/tortoise-qemu-p2"

cleanup() {
  sudo umount "${MNT_P2}" 2>/dev/null || true
  sudo qemu-nbd --disconnect "${NBD}" 2>/dev/null || true
}
trap cleanup EXIT

[[ -f "${BASE}" ]] || { echo "Missing base: ${BASE}" >&2; exit 1; }
[[ -f "${AUTO_XML}" ]] || { echo "Missing autounattend: ${AUTO_XML}" >&2; exit 1; }
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
if [[ ! -d "${MNT_P2}/Windows" ]]; then
  echo "MOUNT_SANITY_FAIL missing ${MNT_P2}/Windows" >&2
  exit 1
fi
sudo mkdir -p "${MNT_P2}/Windows/Panther/Unattend"
sudo cp "${AUTO_XML}" "${MNT_P2}/Windows/Panther/Unattend/unattend.xml"
sudo cp "${AUTO_XML}" "${MNT_P2}/Windows/Panther/Unattend/autounattend.xml"
sudo cp "${AUTO_XML}" "${MNT_P2}/Windows/Panther/unattend.xml"
sync
echo "INJECT_OK path=${MNT_P2}/Windows/Panther/unattend.xml"
