#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
DEVICE="${TORTOISE_QEMU_LAB_DEVICE:-/dev/nvme1n1}"
STORAGE_JSON="${LAB_ROOT}/config/lab-storage.json"
BASE_IMAGE="${LAB_ROOT}/base/windows-base.qcow2"
FINGERPRINT="${LAB_ROOT}/config/base-image.sha256"

if [[ ! -b "${DEVICE}" ]]; then
  echo "LAB_STORAGE_RECREATED reason=azure_local_nvme_missing" >&2
  exit 10
fi

if ! findmnt -n "${LAB_ROOT}" >/dev/null 2>&1; then
  if ! "${SCRIPT_DIR}/mount-lab-disk.sh"; then
    echo "LAB_STORAGE_RECREATED reason=mount_failed" >&2
    exit 10
  fi
fi

mount_dev="$(findmnt -n -o SOURCE --target "${LAB_ROOT}")"
if [[ "${mount_dev}" != "${DEVICE}" ]]; then
  echo "LAB_STORAGE_RECREATED reason=unexpected_mount_source expected=${DEVICE} actual=${mount_dev}" >&2
  exit 10
fi

if [[ ! -f "${STORAGE_JSON}" ]]; then
  echo "LAB_STORAGE_RECREATED reason=lab_storage_marker_missing" >&2
  exit 10
fi

expected_uuid="$(python3 -c "import json; print(json.load(open('${STORAGE_JSON}')).get('filesystemUuid',''))")"
actual_uuid="$(blkid -s UUID -o value "${DEVICE}" 2>/dev/null || true)"
if [[ -n "${expected_uuid}" && -n "${actual_uuid}" && "${expected_uuid}" != "${actual_uuid}" ]]; then
  echo "LAB_STORAGE_RECREATED reason=filesystem_uuid_changed expected=${expected_uuid} actual=${actual_uuid}" >&2
  exit 10
fi

if [[ -f "${FINGERPRINT}" && ! -f "${BASE_IMAGE}" ]]; then
  echo "LAB_STORAGE_RECREATED reason=base_image_missing_with_prior_fingerprint" >&2
  exit 10
fi

echo "LAB_STORAGE_OK lab_root=${LAB_ROOT}"
exit 0
