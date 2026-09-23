# Stabilization and Batch 15

This document tracks audit remediation after Batch 14. The accurate status is: **substantial progress, not complete**.

## Phase 0 — Safety lockdown

- Removed `TORTOISE_VM_MARKER` as disposable-VM evidence
- Real mutation blocked in public `tortoise` / WPF builds via runtime `MutationBuildPolicy`
- Added `Tortoise.Lab` (`tortoise-lab`) for isolated VM mutation testing only
- Physical pilot confirmation remains non-mutating (readiness recording only)
- Added `MutationServicingLock` to prevent concurrent real servicing

**Still open:** compile-time isolation (`Tortoise.MutationLab` split) so public binaries cannot reference real mutation code at all. `tortoise-lab-broker` is now a separate executable absent from public release packaging.

## Phase 1 — Windows interop and WUA

- Fixed ConfigMgr sizing and SetupAPI signatures
- Corrected `CM_Get_Device_ID_List_SizeW` parameter order (`out length` first)
- Corrected DEVPKEY driver property set
- WUA activation via documented ProgID `Microsoft.Update.Session` (not CoClass/IID confusion)
- WUA collections via ProgID `Microsoft.Update.UpdateColl`
- Rebuilt WUA COM layer from Microsoft `wuapi.idl` GUIDs and dispids
- Fixed remaining category interface IDs/dispids (`ICategory`, `ICategoryCollection`)
- `IUpdateInstaller.Install()` returns `IInstallationResult`; explicit `IUpdateDownloader.Download()` before install
- Driver-only properties read from `IWindowsDriverUpdate`, not base `IUpdate`
- `AutoSelection` read from `IUpdate5` with corrected mapping (3=Recommended, 2=Optional)
- Authoritative device matching uses WUA `DriverHardwareID` vs device hardware/compatible IDs
- Unmapped driver scan results produce `Unknown` device state (fail-closed), not `Current`
- Signature/source provenance no longer fabricated as trusted Windows Update

**Still open:** WUA download/install runtime proof at scale requires disposable VM harness runs (harness implemented in Batch 16; hosted CI does not execute real VM mutations). Managed WUA surface has SDK-backed `wuapi.idl` parity tests for mutation-path interfaces.

## Phase 2 — Broker trust

- Broker protocol v2 with capability token and Windows session binding
- Named-pipe ACL (current user only on Windows)
- `BrokerPlanAuthority` reloads persisted plans and validates canonical hash + install payload
- Elevated broker launch via UAC (`BrokerElevationLauncher`) wired into `tortoise-lab vm install` (uses separate `tortoise-lab-broker` executable)
- Elevated broker launch uses `tortoise-lab-broker` (compile-time split from public `Tortoise.Broker`)
- One-shot elevated broker accepts a single authorized `InstallDriver` request (no startup Ping)
- Broker fails closed without live verifier; re-validates stored plan risk/classification independently
- WUA download runs unelevated with persisted `Downloading → Verified` journal states; broker performs install-only boundary
- WUA per-update `GetUpdateResult(0)` inspection; aggregate `SucceededWithErrors` no longer treated as success
- `RealTransactionRecoveryService` for `AwaitingReboot` resume and ambiguous `Installing` reconciliation

- Pending reboot gate at broker boundary (`PendingRebootState`: blocks `Pending` and `Unknown`)
- PID-bound one-shot install pipe; wrong-PID clients disconnected without response
- Separate workflow lock (Lab client) vs servicing lock (elevated broker)

**Still open:** `%ProgramData%` plan store cryptographic sealing (ACL hardening exists); compile-time public/Lab split beyond current Lab executables.

## Phase 3 — Plan integrity and recovery

- Canonical plan hash uses deterministic JSON (not delimiter-joined strings)
- Frozen plans reject overwrite (`INSERT ... ON CONFLICT ... WHERE is_frozen = 0`)
- Physical pilot checklist uses readiness-only preflight (mutation authorization evaluated separately)
- Recovery preparation stale after 24h remains blocked

## Phase 4 / Batch 15–16 — CI, tests, release, VM harness

- Batch 16 adds developer-only `tools/Tortoise.VmHarness/` (Hyper-V checkpoint orchestration, not in public release ZIPs)
- Public verification transparency: `docs/TESTING.md`, README safety section, TRX-based verification summaries

- Fixed invalid GitHub Actions SHA pins (verified 40-character commit IDs from tagged releases)
- Release validation runs on **both** `ubuntu-latest` and `windows-latest`
- Prerelease tags use numeric `AssemblyVersion`/`FileVersion`; full tag in `InformationalVersion`
- Package inventory exported as `dependencies.json` (not SPDX/CycloneDX SBOM)
- Real VM install path writes durable transaction journal entries via `IUpdateTransactionStore`
- Journal persisted before download, verification, elevation, install, and reboot checkpoints
- Shared `UpdateTransactionJournal` enforces transition validation for simulated and real paths
- Reboot-required installs end in `AwaitingReboot` (not falsely `Completed`)

- Release publish uses separate `artifacts/app` and `artifacts/cli` directories (no case-collision overwrite)
- Validation jobs use read-only permissions; attestation/write permissions scoped to release job only

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
