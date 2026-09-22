# Physical Pilot Recovery Guide

This guide describes recovery steps if a physical pilot driver update causes instability.
Tortoise prepares recovery metadata but does not guarantee rollback.

## Before you install

1. Run `tortoise recover prepare <plan-id>` and verify the preparation record.
2. Confirm System Restore is enabled when possible.
3. Export the recovery manifest with `tortoise recover export <preparation-id> <path>`.
4. Complete `tortoise pilot checklist <plan-id>` and record final confirmation.

## If the device misbehaves after install

1. Do not run additional Tortoise install commands.
2. Reboot once if Windows prompts for restart and the device is still reachable.
3. Open Device Manager and verify the target device health and installed driver version.
4. Review the latest Tortoise recovery preparation and exported manifest.

## Rollback options

### Option A: Windows Device Manager

1. Open Device Manager.
2. Locate the target device.
3. Open Properties → Driver → Roll Back Driver if the button is available.
4. Reboot if prompted.

### Option B: System Restore

1. Open System Restore from Recovery settings.
2. Choose a restore point created before the pilot install.
3. Reboot and re-scan the device inventory with Tortoise.

### Option C: Windows Update re-offer

1. Run a fresh Tortoise scan and review whether Windows still offers the previous driver package.
2. Do not force-install an older package unless Windows classifies it as applicable.

## After recovery

1. Run `tortoise scan` and `tortoise devices --problem`.
2. Rebuild plans only after the device state is stable.
3. Document the incident and keep the recovery manifest for review.

## When to stop

Stop and seek manual support if:

- The device remains in a problem state after rollback attempts
- Secure Boot, storage, networking, or boot-critical devices are affected
- You cannot account for the installed driver version or package source
