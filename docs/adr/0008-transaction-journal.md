# ADR 0008: Transaction journal

## Status

Accepted

## Context

Driver updates span reboots and crashes.

## Decision

Persist explicit transaction state in SQLite with crash-safe journaling.

## Consequences

- Reconciliation logic required on startup
- Migrations must be tested across versions
