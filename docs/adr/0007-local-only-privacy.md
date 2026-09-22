# ADR 0007: Local-only privacy

## Status

Accepted

## Context

Driver inventory is sensitive machine configuration data.

## Decision

Default to local-only operation with no telemetry.

## Consequences

- No cloud account or sync in V1
- Diagnostic export is explicit and redacted
