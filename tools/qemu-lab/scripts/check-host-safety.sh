#!/usr/bin/env bash
# Fail-closed host safety checks before starting a Tortoise QEMU lab guest.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
MIN_FREE_RAM_KB="${TORTOISE_QEMU_MIN_FREE_RAM_KB:-8388608}"   # 8 GiB host reserve
MIN_FREE_DISK_KB="${TORTOISE_QEMU_MIN_FREE_DISK_KB:-10485760}" # 10 GiB free on lab FS
MAX_LAB_DIR_KB="${TORTOISE_QEMU_MAX_LAB_DIR_KB:-167772160}"    # 160 GiB ceiling
REQUIRE_KVM="${TORTOISE_QEMU_REQUIRE_KVM:-1}"
ALLOW_TCG="${TORTOISE_QEMU_ALLOW_TCG:-0}"
CURRENT_RUN_JSON="${LAB_ROOT}/artifacts/current-run.json"

fail() { echo "HOST_SAFETY_BLOCKED: $*" >&2; exit 1; }

if [[ "${1:-}" == "--assessment-only" ]]; then
  echo "Assessment-only mode: skipping lab-root and guest-run checks."
  exit 0
fi

"${SCRIPT_DIR}/check-storage-state.sh" || fail "Ephemeral lab storage check failed"

[[ -d "${LAB_ROOT}" ]] || fail "Lab root missing: ${LAB_ROOT}"

available_kb="$(awk '/MemAvailable:/ {print $2}' /proc/meminfo)"
[[ "${available_kb}" -ge "${MIN_FREE_RAM_KB}" ]] || fail "MemAvailable ${available_kb}KB < required ${MIN_FREE_RAM_KB}KB"

lab_fs="$(df -Pk "${LAB_ROOT}" | tail -1 | awk '{print $1}')"
free_kb="$(df -Pk "${LAB_ROOT}" | tail -1 | awk '{print $4}')"
[[ "${free_kb}" -ge "${MIN_FREE_DISK_KB}" ]] || fail "Free disk on ${lab_fs} is ${free_kb}KB < required ${MIN_FREE_DISK_KB}KB"

lab_used_kb="$(du -sk --exclude=lost+found "${LAB_ROOT}" 2>/dev/null | awk '{print $1}')"
[[ -n "${lab_used_kb}" ]] || lab_used_kb="$(find "${LAB_ROOT}" -xdev -printf '%s\n' 2>/dev/null | awk '{s+=$1} END {print int(s/1024)}')"
[[ "${lab_used_kb}" -le "${MAX_LAB_DIR_KB}" ]] || fail "Lab directory ${lab_used_kb}KB exceeds ceiling ${MAX_LAB_DIR_KB}KB"

if [[ -f "${CURRENT_RUN_JSON}" ]]; then
  if [[ -f "${LAB_ROOT}/tmp/qemu.pid" ]] && pgrep -F "${LAB_ROOT}/tmp/qemu.pid" >/dev/null 2>&1; then
    fail "Another Tortoise lab QEMU instance appears active (${CURRENT_RUN_JSON})"
  fi
fi

if [[ "${REQUIRE_KVM}" == "1" ]]; then
  [[ -e /dev/kvm ]] || fail "/dev/kvm missing"
  if [[ -r /dev/kvm && -w /dev/kvm ]]; then
    : ok
  elif sg kvm -c 'test -r /dev/kvm && test -w /dev/kvm' 2>/dev/null; then
    : ok_via_sg
  else
    fail "/dev/kvm not readable/writable by current user (add to kvm group?)"
  fi
elif [[ "${ALLOW_TCG}" != "1" ]]; then
  fail "KVM unavailable and TCG not explicitly allowed (TORTOISE_QEMU_ALLOW_TCG=1)"
fi

lab_root_real="$(realpath "${LAB_ROOT}")"
for var in TORTOISE_QEMU_BASE_IMAGE TORTOISE_QEMU_OVERLAY_IMAGE; do
  val="${!var:-}"
  [[ -z "${val}" ]] && continue
  val_real="$(realpath "${val}")"
  [[ "${val_real}" == "${lab_root_real}"/* ]] || fail "${var}=${val_real} escapes lab root ${lab_root_real}"
done

for forbidden in TORTOISE_QEMU_HOST_PASSTHROUGH TORTOISE_QEMU_VIRTFS TORTOISE_QEMU_RAW_DISK; do
  [[ -z "${!forbidden:-}" ]] || fail "Forbidden env set: ${forbidden}"
done

echo "HOST_SAFETY_OK lab_root=${lab_root_real} mem_available_kb=${available_kb} disk_free_kb=${free_kb} lab_used_kb=${lab_used_kb}"
