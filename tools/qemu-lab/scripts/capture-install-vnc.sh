#!/usr/bin/env bash
# Boot lab guest with VNC and capture PNG for install diagnosis.
set -euo pipefail
LAB="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
OUT="${LAB}/artifacts/install-debug-$(date -u +%Y%m%dT%H%M%SZ).png"
mkdir -p "${LAB}/artifacts"
vncsnapshot -quiet 127.0.0.1:9 "$OUT"
echo "SCREENSHOT=$OUT"
