# QEMU Lab Host Assessment — ZealVPS (updated)

**Assessed:** 2026-09-23T06:20:00Z  
**Verdict:** **`SAFE_FOR_KVM_LAB`**  
**Proof status:** **`QEMU_LAB_READY`** (Windows base install + overlay proof in progress)

---

## NVMe preflight (completed)

See [`nvme-preflight.md`](nvme-preflight.md) and [`nvme-preflight.json`](nvme-preflight.json).

**Summary:** `/dev/nvme1n1` (220 GiB, `Microsoft NVMe Direct Disk v2`, Azure local index 1) was **unused** — no partitions, no filesystem, not mounted, not in LVM/RAID/swap/fstab. Root `/` is on **`/dev/nvme0n1p1`**, not nvme1n1.

**Provisioned:**

| Item | Value |
|------|--------|
| Device | `/dev/nvme1n1` |
| Filesystem | ext4, label `TORTOISE_QEMU_LA` |
| UUID | `23055e49-fba3-4068-b415-eafbd49aa809` |
| Mount | `/mnt/tortoise-qemu-lab` |
| fstab entry | **none** (ephemeral Azure disk) |
| Lab ceiling | **160 GiB** directory quota target |

---

## CPU / KVM

| Item | Value |
|------|--------|
| CPU | 4 vCPU AMD EPYC 9V45 |
| Nested virt | `nested=1` |
| `/dev/kvm` | present |
| User `lapie` in `kvm` group | **yes** (added; use `sg kvm` until re-login) |

---

## Memory

| Item | Value |
|------|--------|
| Total | 15.6 GiB |
| MemAvailable | ~12 GiB |
| Host reserve | **8 GiB** |
| Guest allocation | **4 GiB**, **2 vCPU** |

---

## Disk

| Location | Free | Role |
|----------|------|------|
| `/` (OS) | ~24 GiB | Existing projects untouched |
| `/mnt/tortoise-qemu-lab` | ~214 GiB | Disposable Windows lab bulk storage |

---

## Packages installed (host)

- `qemu-system-x86`, `qemu-utils`, `ovmf`, `wimtools`

**No host reboot.** No other disks modified.

---

## Guest OS plan

**Windows Server 2025 Datacenter Evaluation** (Desktop Experience, WIM index 4)  
ISO: official Microsoft Eval Center redirect (`linkid=2293312`) → stored at  
`/mnt/tortoise-qemu-lab/iso/windows-server-2025-eval.iso` (5.7 GiB)

Install method: **legacy BIOS (`-machine pc`)** + floppy `autounattend.xml` (MBR layout). OVMF boot path did not reliably start the ISO.

---

## Ephemeral storage policy

Azure temporary NVMe may be blank after stop/deallocate. Lab scripts emit **`LAB_STORAGE_RECREATED`** if device/mount/base marker missing. Durable scripts and docs remain on the OS disk / Tortoise repo.

---

## Next steps (automated pipeline running)

1. Finish unattended Windows base install  
2. Seal `config/base-image.sha256`  
3. Run `prove-overlay-cycle.sh`  
4. Target proof status: **`DISPOSABLE_OVERLAY_PROVED`**

**No Tortoise driver mutation.**
