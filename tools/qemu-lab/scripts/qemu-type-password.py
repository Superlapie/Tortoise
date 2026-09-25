#!/usr/bin/env python3
import json
import os
import socket
import sys
import time

lab_root = os.environ.get("TORTOISE_QEMU_LAB_ROOT", "/mnt/tortoise-qemu-lab")
creds_path = os.environ.get(
    "TORTOISE_QEMU_LAB_CREDENTIALS",
    os.path.join(lab_root, "config", "lab-credentials.json"),
)
monitor_port = int(os.environ.get("TORTOISE_QEMU_MONITOR_PORT", "4444"))
password = json.load(open(creds_path))["adminPassword"]


def monitor_cmd(cmd: str) -> None:
    sock = socket.create_connection(("127.0.0.1", monitor_port), timeout=5)
    sock.settimeout(1)
    buf = b""
    for _ in range(20):
        try:
            chunk = sock.recv(4096)
            if not chunk:
                break
            buf += chunk
            if b"(qemu)" in buf:
                break
        except socket.timeout:
            if b"(qemu)" in buf:
                break
    sock.sendall((cmd + "\n").encode("ascii"))
    time.sleep(0.05)
    sock.close()


for ch in password:
    if ch.isupper():
        monitor_cmd(f"sendkey shift-{ch.lower()}")
    elif ch.isdigit():
        monitor_cmd(f"sendkey {ch}")
    else:
        monitor_cmd(f"sendkey {ch}")
monitor_cmd("sendkey ret")
print("password_typed")
