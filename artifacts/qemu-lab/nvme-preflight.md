# NVMe Preflight — Azure Temporary Lab Disk

**Assessed:** 2026-09-23T05:34:00Z  
**Host:** ZealVPS (`Standard_D4ads_v7`, eastus)  
**Verdict:** **SAFE_TO_FORMAT**

Read-only checks completed before any write to `/dev/nvme1n1`.

---

## Root / boot storage (NOT nvme1n1)

| Mount | Source | Device model |
|-------|--------|--------------|
| `/` | `/dev/nvme0n1p1` (ext4) | MSFT NVMe Accelerator v1.0 (128 GiB managed OS) |
| `/boot` | `/dev/nvme0n1p16` | same disk |
| `/boot/efi` | `/dev/nvme0n1p15` (vfat) | same disk |
| swap | `/swapfile` on `/` | not a block device |

`findmnt /` → `/dev/nvme0n1p1`  
Azure symlink `/dev/disk/azure/os` → `nvme0n1`  
**Root is provably NOT `/dev/nvme1n1`.**

---

## Candidate lab disk: `/dev/nvme1n1`

| Field | Value |
|-------|--------|
| Size | 220 GiB (236,223,201,280 bytes) |
| Model | **Microsoft NVMe Direct Disk v2** |
| Serial | `b04204e8301f90100001` |
| Partitions | **none** |
| FSTYPE | **none** |
| MOUNTPOINTS | **none** |
| blkid | **no output** |
| First 4 KiB | **all zeros** (never formatted) |
| LVM (pvs/vgs/lvs) | **empty** |
| RAID (/proc/mdstat) | **none** |
| fstab reference | **none** |
| fuser / lsof | **no open users** |
| device-mapper | **no references** |

### Azure local disk identity

```
/dev/disk/azure/local/by-index/1        -> nvme1n1
/dev/disk/azure/local/by-name/nvme-220G-1 -> nvme1n1
/dev/disk/azure/local/by-serial/b04204e8301f90100001 -> nvme1n1
```

This matches Azure **local temporary NVMe** included with `Standard_D4ads_v7` (220 GiB ephemeral resource).

Azure metadata `storageProfile.resourceDisk.size` reports `"0"` (known metadata quirk); physical device is present as above.

Managed data disks: **[]** (none attached).

---

## Stop conditions evaluated

| Condition | Result |
|-----------|--------|
| Unexpected filesystem on nvme1n1 | **No** — pass |
| Partitions with data | **No** — pass |
| LVM/RAID/swap participation | **No** — pass |
| Mounted | **No** — pass |
| Open workloads | **No** — pass |
| Ambiguous identity | **No** — Azure local links + model name conclusive |
| Root/boot on nvme1n1 | **No** — pass |

---

## Planned provisioning (after this document)

1. `mkfs.ext4 -L TORTOISE_QEMU_LAB` on **whole disk** `/dev/nvme1n1` (no repartitioning)
2. Mount at `/mnt/tortoise-qemu-lab`
3. **No `/etc/fstab` entry** (ephemeral; host must not fail boot if disk disappears)
4. Lab ceiling **160 GiB**; leave ~50+ GiB headroom on device
5. Only regenerable bulk data on this volume (ISO, qcow2, caches)

Durable scripts, hashes, and reconstruction metadata remain on the OS disk / Tortoise repo.
