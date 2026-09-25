#!/usr/bin/env python3
import socket
import sys

host = sys.argv[1] if len(sys.argv) > 1 else "127.0.0.1"
port = int(sys.argv[2]) if len(sys.argv) > 2 else 53389
classic_probe = bytes(
    [0x03, 0x00, 0x00, 0x13, 0x0E, 0xE0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00]
)
tls_probe = bytes([0x16, 0x03, 0x03, 0x00, 0x05, 0x01, 0x00, 0x00, 0x01, 0x03])


def looks_like_rdp(data: bytes) -> bool:
    if not data:
        return False
    if data.startswith(b"\x03\x00"):
        return True
    if data[0] == 0x16 and len(data) >= 2 and data[1] in (0x03, 0x02, 0x01):
        return True
    return False


def probe_once(payload: bytes | None) -> bytes:
    sock = socket.socket()
    sock.settimeout(5)
    try:
        sock.connect((host, port))
        sock.settimeout(3)
        banner = b""
        try:
            banner = sock.recv(64)
        except socket.timeout:
            banner = b""
        if looks_like_rdp(banner):
            return banner
        if payload:
            sock.send(payload)
            try:
                return sock.recv(64)
            except socket.timeout:
                return b""
        return banner
    finally:
        sock.close()


try:
    for payload in (None, classic_probe, tls_probe):
        data = probe_once(payload)
        if looks_like_rdp(data):
            print("RDP_OK")
            raise SystemExit(0)
    print(repr(data) if data else "EMPTY")
    raise SystemExit(1)
except OSError as exc:
    print(f"ERR {type(exc).__name__}")
    raise SystemExit(1)
