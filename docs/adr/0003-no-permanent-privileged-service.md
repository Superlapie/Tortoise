# ADR 0003: No permanent privileged service

## Status

Accepted

## Context

Always-running SYSTEM services increase attack surface.

## Decision

V1 uses a short-lived elevated broker process, not a permanent privileged service.

## Consequences

- Broker must restart per transaction
- Simpler lifecycle, stronger isolation
- Scheduled scans run unelevated read-only in future versions
