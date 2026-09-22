# Stabilization and Batch 15

This document tracks audit remediation after Batch 14. The accurate status is: **substantial progress, not complete**.

## Phase 0 — Safety lockdown

- Removed `TORTOISE_VM_MARKER` as disposable-VM evidence
- Real mutation blocked in public `tortoise` / WPF builds via runtime `MutationBuildPolicy`
- Added `Tortoise.Lab` (`tortoise-lab`) for isolated VM mutation testing only
- Physical pilot confirmation remains non-mutating (readiness recording only)
- Added `MutationServicingLock` to prevent concurrent real servicing

**Still open:** compile-time isolation (`Tortoise.MutationLab` split) so public binaries cannot reference real mutation code at all.

## Phase 1 — Windows interop and WUA

- Fixed ConfigMgr sizing and SetupAPI signatures
- Corrected DEVPKEY driver property set
- Rebuilt WUA COM layer from Microsoft `wuapi.idl` GUIDs and dispids
- `IUpdateInstaller.Install()` now returns `IInstallationResult` (not `int`)
- Driver-only properties read from `IWindowsDriverUpdate`, not base `IUpdate`
- `AutoSelection` read from `IUpdate5` with corrected mapping (3=Recommended, 2=Optional)
- Authoritative device matching uses WUA `DriverHardwareID` vs device hardware/compatible IDs
- Unmapped driver scan results produce `Unknown` device state (fail-closed), not `Current`
- Signature/source provenance no longer fabricated as trusted Windows Update

**Still open:** Windows CI verification of COM interop on real `windows-latest` runners.

## Phase 2 — Broker trust

- Broker protocol v2 with capability token and Windows session binding
- Named-pipe ACL (current user only on Windows)
- `BrokerPlanAuthority` reloads persisted plans and validates canonical hash + install payload
- Elevated broker launch via UAC (`BrokerElevationLauncher`) wired into `tortoise-lab vm install`
- Broker TOCTOU guard re-checks device presence, installed driver version, live WUA candidate, hardware ID, and classification before install (Windows broker host)

**Still open:** `%ProgramData%` plan store ACL / cryptographic sealing; pending reboot gate at broker boundary.

## Phase 3 — Plan integrity and recovery

- Canonical plan hash uses deterministic JSON (not delimiter-joined strings)
- Frozen plans reject overwrite (`INSERT ... ON CONFLICT ... WHERE is_frozen = 0`)
- Physical pilot checklist uses readiness-only preflight (mutation authorization evaluated separately)
- Recovery preparation stale after 24h remains blocked

## Phase 4 / Batch 15 — CI, tests, release

- Fixed invalid GitHub Actions SHA pins (verified 40-character commit IDs from tagged releases)
- Release validation runs on **both** `ubuntu-latest` and `windows-latest`
- Prerelease tags use numeric `AssemblyVersion`/`FileVersion`; full tag in `InformationalVersion`
- Package inventory exported as `dependencies.json` (not SPDX/CycloneDX SBOM)
- Real VM install path now writes durable transaction journal entries via `IUpdateTransactionStore`

**Still open:** signed release binaries, branch protection, clean-VM release smoke test, real package export.

## Usage

Public read-only builds:

```bash
tortoise status
tortoise scan
```

Lab mutation (disposable VM + env markers only):

```bash
TORTOISE_MUTATION_TESTS=1
TORTOISE_ALLOW_VM_INSTALL=1
tortoise-lab vm install <plan-id>
```

Do **not** run real mutation on a physical production machine.
