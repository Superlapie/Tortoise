#!/usr/bin/env bash
# Mount Azure ephemeral lab NVMe if present. Safe no-op if already mounted.
# Intentionally NOT added to /etc/fstab — disk may disappear after deallocate.
set -euo pipefail

DEVICE="${TORTOISE_QEMU_LAB_DEVICE:-/dev/nvme1n1}"
MOUNT_POINT="${TORTOISE_QEMU_LAB_MOUNT:-/mnt/tortoise-qemu-lab}"

if findmnt -n "${MOUNT_POINT}" >/dev/null 2>&1; then
  echo "LAB_MOUNT_OK already_mounted=${MOUNT_POINT}"
  exit 0
fi

[[ -b "${DEVICE}" ]] || {
  echo "LAB_STORAGE_RECREATED reason=device_missing path=${DEVICE}" >&2
  exit 2
}

sudo mkdir -p "${MOUNT_POINT}"

if ! blkid "${DEVICE}" | grep -q 'TYPE="ext4"'; then
  echo "LAB_STORAGE_RECREATED reason=unformatted_or_wrong_fs device=${DEVICE}" >&2
  exit 3
fi

sudo mount -o nodev,nosuid,relatime "${DEVICE}" "${MOUNT_POINT}"
sudo chown "${USER}:${USER}" "${MOUNT_POINT}" 2>/dev/null || true
echo "LAB_MOUNT_OK mounted=${MOUNT_POINT} device=${DEVICE}"
