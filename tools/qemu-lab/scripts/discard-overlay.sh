#!/usr/bin/env bash
# Discard a disposable overlay; never deletes paths outside LAB_ROOT.
set -euo pipefail

LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
OVERLAY="${1:-}"

[[ -n "${OVERLAY}" ]] || { echo "Usage: discard-overlay.sh <overlay.qcow2>" >&2; exit 1; }

lab_root_real="$(realpath "${LAB_ROOT}")"
overlay_real="$(realpath "${OVERLAY}")"

[[ "${overlay_real}" == "${lab_root_real}"/* ]] || {
  echo "Refusing to delete outside lab root: ${overlay_real}" >&2
  exit 1
}

[[ -f "${overlay_real}" ]] || {
  echo "Overlay already absent: ${overlay_real}"
  exit 0
}

rm -f "${overlay_real}"
echo "OVERLAY_DISCARDED path=${overlay_real}"
