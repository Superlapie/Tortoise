# Tortoise QEMU Lab (Linux host)

Developer-only disposable Windows testing on a **Linux** host using **QEMU/KVM** and **qcow2 overlays**. This complements the existing **Hyper-V** harness on Windows; it does not replace it.

> **Status on ZealVPS (2026-09-23):** NVMe lab provisioned at `/mnt/tortoise-qemu-lab`. Verdict **`SAFE_FOR_KVM_LAB`**. Windows base install + overlay proof pipeline **running** — see [`artifacts/qemu-lab/host-assessment.md`](../artifacts/qemu-lab/host-assessment.md).

## Why this exists

The Tortoise VM harness needs a disposable Windows environment for evidence gathering. Hyper-V fits a Windows Hyper-V host. This Azure Linux dev VM (`Standard_D4ads_v7`) can potentially run nested KVM, but only with:

1. Adequate **dedicated disk** for Windows base + overlays
2. **KVM access** for the lab user (`kvm` group)
3. Strict **isolation** from other projects on the same host

## Isolation model

| Allowed | Forbidden |
|---------|-----------|
| Lab root under `/mnt/tortoise-qemu-lab/` on Azure ephemeral NVMe | VirtFS/9p host project mounts |
| qcow2 base + disposable overlays inside lab root | SMB shares to important host dirs |
| QEMU user-mode NAT | Bridged networking / host NIC changes |
| localhost-only forwarded ports | `0.0.0.0` management binds |
| Non-root QEMU when possible | Raw disk passthrough, `/dev/sdX` |
| | Docker socket, cloud creds, host home sharing |

## KVM vs TCG

| Mode | When | Windows install realistic? |
|------|------|----------------------------|
| **KVM** (`-accel kvm`) | `/dev/kvm` accessible, nested virt enabled | Yes (modest 4 vCPU / 4 GiB guest) |
| **TCG** (software) | KVM unavailable | **No** for Windows 11 install on this host |

ZealVPS has **`nested=1`** and `/dev/kvm`, but the lab user is not yet in the `kvm` group.

## Host resource requirements (conservative)

| Resource | Host reserve | Lab allocation |
|----------|--------------|----------------|
| RAM | 8 GiB minimum free | 4 GiB guest (Windows minimum) |
| Disk | 10 GiB free on lab filesystem | ≤45 GiB lab directory ceiling |
| CPU | leave ≥2 vCPUs for host | 2 vCPUs for guest initially |

## Directory layout (when provisioned)

```
/mnt/tortoise-qemu-lab/          # Azure local NVMe (ephemeral)
  base/windows-base.qcow2
  overlays/run-<id>.qcow2
  iso/windows-server-2025-eval.iso
  config/lab-storage.json        # device UUID + ceiling
  config/base-image.sha256       # sealed after install
  artifacts/overlay-proof.json   # after disposable overlay proof
```

Repo scripts: `tools/qemu-lab/scripts/` (copy or invoke from repo; durable on OS disk).

## Base image / overlay model (not Hyper-V checkpoints)

| Hyper-V harness | QEMU lab |
|-----------------|----------|
| Hyper-V checkpoint | Disposable qcow2 overlay over sealed base |
| Restore checkpoint | **Discard overlay** (delete file) |
| Checkpoint ID | Overlay path + backing-file fingerprint |

The harness provider `QemuVmHarnessProvider` maps harness checkpoint APIs to **overlay create/discard** language internally. Reports should say **overlay**, not Hyper-V checkpoint.

## Networking

Use **QEMU user-mode NAT**:

```
-netdev user,id=n0,hostfwd=tcp:127.0.0.1:53389-:3389
```

RDP/WinRM only on **127.0.0.1** high ports unless separately approved.

## Guest OS choice (future install)

| Option | Notes |
|--------|-------|
| **Windows Server 2025 Evaluation** | Supported WUA/driver stack; simpler TPM posture than Win11 desktop |
| **Windows 11** | Requires TPM/OVMF/swtpm planning; more moving parts |

Use **legitimate Microsoft evaluation ISOs** only. Do not download third-party repacks.

## Safety guardrails

Shell: `tools/qemu-lab/scripts/check-host-safety.sh`  
C#: `QemuLabHostSafety` + unit tests in `Tortoise.VmHarness.Tests`

Checks before every launch:

- minimum host RAM/disk
- lab directory size ceiling
- paths confined to lab root
- no passthrough env vars
- single active QEMU run
- KVM access or explicit TCG opt-in

## Workflow (when unblocked)

1. Host assessment passes (`SAFE_FOR_KVM_LAB`)
2. Install minimal packages: `qemu-system-x86`, `qemu-utils`, `ovmf`
3. Create lab root + install/seal Windows base (manual/automation — no Tortoise driver mutation)
4. Record base fingerprint (`QemuBaseImageFingerprint`)
5. Create disposable overlay (`qemu-img create -f qcow2 -F qcow2 -b base ...`)
6. Boot overlay, reach guest, collect evidence
7. Stop guest, discard overlay
8. Create fresh overlay, boot again, prove base fingerprint unchanged

## Provider integration

| Provider | Host | Identity binding |
|----------|------|------------------|
| `HyperVVmHarnessProvider` | Windows Hyper-V | Hyper-V VM GUID |
| `QemuVmHarnessProvider` | Linux QEMU/KVM | Run ID + overlay path + base fingerprint |

Not production-ready until a real disposable overlay proof completes on the host.

## Double proof (future mutation gate)

**Host proof:** expected base image path, base SHA-256 fingerprint, overlay backing file matches base, QEMU PID recorded, disposable marker present.

**Guest proof:** Windows VM detection, Tortoise.Lab build, mutation env markers, run nonce if configured.

If either side is unknown, the harness **blocks**.

## What this does not protect against

- Host disk full outside lab if ceilings are wrong
- Operator bypassing safety scripts
- Malicious Windows guest targeting host (keep networking NAT + no shares)
- Azure host maintenance reboots

## ZealVPS assessment summary

See [`artifacts/qemu-lab/host-assessment.json`](../artifacts/qemu-lab/host-assessment.json).

**Verdict:** `INSUFFICIENT_RESOURCES` (23.5 GiB free on `/`, need ~45 GiB lab ceiling)

**Unblock options (require explicit approval):**

- Attach/format 220 GiB data disk (`nvme1n1`) as dedicated lab volume
- Free ≥45 GiB on `/`
- Add user to `kvm` group + install QEMU packages (no reboot)
