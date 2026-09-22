# Changelog

All notable changes to Tortoise are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Invalid GitHub Actions SHA pins that prevented build/test/CodeQL from starting
- WUA COM interface GUIDs, dispids, `IInstallationResult` install path, and reversed `AutoSelection` classification
- Hardware-ID matching now uses authoritative WUA `DriverHardwareID` instead of title/description heuristics
- Fail-closed `Unknown` device state when driver updates cannot be mapped; honest signature/source provenance
- Frozen plan overwrite protection, canonical JSON plan hash, physical pilot readiness-only preflight
- Broker TOCTOU re-checks, elevated broker launch in lab, VM install transaction journaling
- Release workflow: Windows + Linux validation, prerelease assembly versioning, `dependencies.json` naming

### Added

- Stabilization remediation (partial): Tortoise.Lab mutation isolation, WUA rebuild, broker v2, CI/release hardening
- Batch 15 release workflow with dual-OS validation gate, package inventory export, SHA-256 checksums, attestation hook
- Batch 14 physical pilot readiness checklist, confirmation recording, recovery guide, and WPF Pilot page
- Environment marker `TORTOISE_PHYSICAL_PILOT=1` and CLI commands `tortoise pilot checklist`, `tortoise pilot confirm`, and `tortoise pilot recovery-doc`
- Batch 13 fault injection scenarios (process crash, unexpected reboot, network failure, candidate disappearance) with reconciliation
- Environment marker `TORTOISE_FAULT_INJECTION=1` and CLI commands `tortoise fault list`, `tortoise fault run`, and `tortoise fault reconcile`
- Batch 12 VM-gated Windows Update driver install for one low-risk plan via broker `InstallDriver` and WUA
- Environment markers `TORTOISE_MUTATION_TESTS`, `TORTOISE_ALLOW_VM_INSTALL`, and optional `TORTOISE_VM_MARKER`
- CLI commands: `tortoise vm status` and `tortoise vm install <plan-id>`
- Batch 11 end-to-end simulated mutation workflow with package verification, post-install verification, and optional broker plan validation
- CLI command: `tortoise workflow run <plan-id> [--skip-broker] [--session-id=N] [--pipe=name]`
- `MutationCapability.Simulated` preset for simulation-only paths (mutation remains disabled)
- Batch 10 one-shot broker with named pipe IPC, nonce replay protection, and allowlisted operations (no driver install)
- CLI commands: `tortoise broker ping`, `tortoise broker status`, and `tortoise broker serve`
- Batch 9 recovery preparation with before snapshots, export abstraction, and recovery manifest export
- CLI commands: `tortoise recover prepare`, `tortoise recover list`, and `tortoise recover export`
- Batch 8 frozen update plans, staleness checks, preflight skeleton, and simulated transactions without mutation
- CLI commands: `tortoise plan`, `tortoise plans`, `tortoise preflight`, and `tortoise simulate`
- Batch 7 SQLite persistence with explicit migrations and scan session storage under `%ProgramData%\\Tortoise\\`
- Redacted JSON diagnostics export via `tortoise export-report`
- Batch 6 WPF UI with Overview, Devices, Updates, Safety, History, Settings, and About pages
- Light/dark theme switching, scan coordination, and in-memory session history
- Batch 5 recommendation engine combining device inventory, WUA results, and risk policy
- Conservative applicability matching that never prefers higher version numbers alone
- `tortoise recommend [--optional]` CLI command

## [0.1.0-alpha] - TBD

First read-only alpha release milestone (see ROADMAP.md).
