# ADR 0005: No Driver Store cleanup

## Status

Accepted

## Context

Automatic driver package removal can destroy recovery paths.

## Decision

Do not implement automatic Driver Store cleanup in initial releases.

## Consequences

- Disk usage may grow on some systems
- Safer rollback and recovery posture
