# ADR 0002: Windows Update as primary source

## Status

Accepted

## Context

Driver installation from arbitrary internet sources is a primary failure mode for driver utilities.

## Decision

V1 driver installation uses **Windows Update only**.

## Consequences

- Respects enterprise WSUS/MDM policy
- Limits feature scope initially
- Official vendor providers require separate security review before enablement
