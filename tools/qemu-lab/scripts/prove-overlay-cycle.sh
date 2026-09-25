#!/usr/bin/env bash
# Seal base fingerprint and prove disposable overlay create/discard cycle.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
BASE="${LAB_ROOT}/base/windows-base.qcow2"
FINGERPRINT="${LAB_ROOT}/config/base-image.sha256"
PROOF_JSON="${LAB_ROOT}/artifacts/overlay-proof.json"

export TORTOISE_QEMU_LAB_ROOT="${LAB_ROOT}"
export TORTOISE_QEMU_MAX_LAB_DIR_KB="${TORTOISE_QEMU_MAX_LAB_DIR_KB:-167772160}"

"${SCRIPT_DIR}/check-storage-state.sh"
"${SCRIPT_DIR}/check-host-safety.sh"

[[ -f "${BASE}" ]] || { echo "Missing base image ${BASE}" >&2; exit 1; }
base_bytes="$(stat -c%s "${BASE}")"
[[ "${base_bytes}" -gt 2000000000 ]] || {
  echo "Base image too small (${base_bytes} bytes); Windows install incomplete" >&2
  exit 1
}

if [[ -f "${FINGERPRINT}" ]]; then
  BASE_SHA="$(cat "${FINGERPRINT}")"
else
  BASE_SHA="$(sha256sum "${BASE}" | awk '{print $1}' | tee "${FINGERPRINT}")"
fi

RUN1="overlay-proof-1"
RUN2="overlay-proof-2"
OVERLAY1="${LAB_ROOT}/overlays/${RUN1}.qcow2"
OVERLAY2="${LAB_ROOT}/overlays/${RUN2}.qcow2"

rm -f "${OVERLAY1}" "${OVERLAY2}"
qemu-img create -f qcow2 -F qcow2 -b "${BASE}" "${OVERLAY1}"
BACK1="$(qemu-img info --output=json "${OVERLAY1}" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("backing-filename",""))')"
[[ "$(realpath "${BACK1}")" == "$(realpath "${BASE}")" ]] || { echo "Backing chain mismatch overlay1" >&2; exit 1; }

export TORTOISE_QEMU_BASE_IMAGE="${BASE}"
export TORTOISE_QEMU_OVERLAY_IMAGE="${OVERLAY1}"
"${SCRIPT_DIR}/start-lab.sh" "${RUN1}"
"${SCRIPT_DIR}/wait-guest-rdp.sh" 900
"${SCRIPT_DIR}/stop-lab.sh"
"${SCRIPT_DIR}/discard-overlay.sh" "${OVERLAY1}"

qemu-img create -f qcow2 -F qcow2 -b "${BASE}" "${OVERLAY2}"
export TORTOISE_QEMU_OVERLAY_IMAGE="${OVERLAY2}"
"${SCRIPT_DIR}/start-lab.sh" "${RUN2}"
"${SCRIPT_DIR}/wait-guest-rdp.sh" 900
"${SCRIPT_DIR}/stop-lab.sh"
"${SCRIPT_DIR}/discard-overlay.sh" "${OVERLAY2}"

BASE_SHA_AFTER="$(sha256sum "${BASE}" | awk '{print $1}')"
[[ "${BASE_SHA}" == "${BASE_SHA_AFTER}" ]] || { echo "Base fingerprint changed" >&2; exit 1; }

cat > "${PROOF_JSON}" <<EOF
{
  "provedAtUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "baseImagePath": "${BASE}",
  "baseSha256": "${BASE_SHA}",
  "overlayRuns": ["${RUN1}", "${RUN2}"],
  "backingChainVerified": true,
  "baseUnchangedAfterOverlayCycles": true,
  "proofStatus": "DISPOSABLE_OVERLAY_PROVED"
}
EOF

echo "DISPOSABLE_OVERLAY_PROVED base_sha256=${BASE_SHA}"
