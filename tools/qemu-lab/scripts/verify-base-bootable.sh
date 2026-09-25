#!/usr/bin/env bash
# Verify the base qcow2 has boot files and a Windows directory before sealing.
set -euo pipefail

BASE="${1:-${TORTOISE_QEMU_BASE_IMAGE:-/mnt/tortoise-qemu-lab/base/windows-base.qcow2}}"
NBD="/dev/nbd0"
MNT_P1="/tmp/tortoise-qemu-p1"
MNT_P2="/tmp/tortoise-qemu-p2"

cleanup() {
  sudo umount "${MNT_P1}" "${MNT_P2}" 2>/dev/null || true
  sudo qemu-nbd --disconnect "${NBD}" 2>/dev/null || true
}
trap cleanup EXIT

[[ -f "${BASE}" ]] || { echo "Missing base: ${BASE}" >&2; exit 1; }

sudo modprobe nbd max_part=8
sudo qemu-nbd --disconnect "${NBD}" 2>/dev/null || true
sudo qemu-nbd --connect="${NBD}" "${BASE}"
sleep 2

sudo mkdir -p "${MNT_P1}" "${MNT_P2}"
sudo mount -o ro "${NBD}p1" "${MNT_P1}"
sudo mount -o ro "${NBD}p2" "${MNT_P2}"

ok=1
if [[ ! -f "${MNT_P1}/bootmgr" && ! -f "${MNT_P1}/Boot/BCD" ]]; then
  echo "BOOT_CHECK_FAIL missing bootmgr/BCD on p1" >&2
  ok=0
fi
if [[ ! -d "${MNT_P2}/Windows" ]]; then
  echo "BOOT_CHECK_FAIL missing ${MNT_P2}/Windows" >&2
  ok=0
fi

if [[ "${ok}" == "1" ]]; then
  echo "BOOT_CHECK_OK base=${BASE}"
  exit 0
fi
exit 1
