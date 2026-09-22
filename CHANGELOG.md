# Changelog

All notable changes to Tortoise are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Stabilization remediation: Tortoise.Lab mutation isolation, Windows interop/WUA rebuild, broker v2, canonical plan hash, CI/release hardening
- Batch 15 release workflow with test gate, SBOM export, SHA-256 checksums, artifact attestation hook, and pinned Actions
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
