# ADR 0009: No automatic self-update

## Status

Accepted

## Context

Compromised self-updaters are a common privilege escalation vector.

## Decision

V1 may check GitHub Releases and open the release page; no automatic unsigned self-update.

## Consequences

- Users update manually initially
- Signed self-update requires separate security architecture later
