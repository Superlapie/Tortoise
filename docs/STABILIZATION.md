# Stabilization and Batch 15

This document summarizes the audit remediation work applied after Batch 14.

## Phase 0 — Safety lockdown

- Removed `TORTOISE_VM_MARKER` as disposable-VM evidence
- Real mutation is structurally disabled in public `tortoise` / WPF builds via `MutationBuildPolicy`
- Added `Tortoise.Lab` (`tortoise-lab`) for isolated VM mutation testing only
- Physical pilot confirmation remains non-mutating (readiness recording only)
- Added `MutationServicingLock` to prevent concurrent real servicing

## Phase 1 — Windows interop and WUA rebuild

- Fixed `CM_Get_Device_ID_ListW` sizing via `CM_Get_Device_ID_List_SizeW`
- Fixed `SetupDiCreateDeviceInfoList` signature (`GUID*, HWND`)
- Corrected DEVPKEY driver provider/version/date/INF property set
- Replaced WUA COM GUIDs/interfaces with Microsoft-documented values
- Fixed zero-based WUA collection indexing
- Added `IWindowsDriverUpdate` mapping (hardware ID, provider, driver date)
- Enabled SQLite `PRAGMA foreign_keys = ON`
- Fixed persistence test DB cleanup (`SqliteConnection.ClearAllPools`)

## Phase 2 — Broker trust

- Broker protocol v2 with capability token and Windows session binding
- Restricted named-pipe ACL (current user only on Windows)
- `BrokerPlanAuthority` reloads persisted plans and validates canonical hash + install payload
- EULA-required plans blocked at broker authority layer

## Phase 3 — Plan integrity and recovery gates

- Canonical plan hash covers device identity, hardware IDs, candidate identity, classification, risk, restart/EULA
- Physical pilot recovery preparation stale after 24h is blocked
- Failed export + disabled System Restore is now a blocker (not a warning)

## Phase 4 — CI, tests, and release engineering (Batch 15)

- CodeQL runs after explicit `dotnet build`
- Empty Security/Integration test projects now fail CI (`FailWhenNoTestsFound=true`)
- Added Security and Integration test suites
- Release workflow: validate job, semver from tag, SBOM JSON, SHA-256 manifest, attestation hook, pinned Actions
- Protected release environment placeholder (`environment: release`)

## Still intentionally deferred

- Real driver package export (export service remains disabled in public builds)
- Full durable transaction journal wrapping for VM install (simulated path retains journal)
- AC/battery preflight gate
- Signed release binaries (signing hook scaffold only via attestation workflow)
- GitHub branch protection (repository setting)

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

Do **not** run `tortoise-lab` on a physical production machine.
