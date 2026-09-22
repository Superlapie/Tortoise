# Changelog

All notable changes to Tortoise are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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
