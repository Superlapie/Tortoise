using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Mutation;
using Tortoise.Core.Planning;
using Tortoise.Core.Recovery;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Pilot;

public sealed class PhysicalPilotChecklistService : IPhysicalPilotChecklistService
{
    private readonly IExecutionEnvironmentDetector _environmentDetector;
    private readonly IUpdatePlanService _planService;
    private readonly IUpdatePreflightService _preflightService;
    private readonly IDeviceInventoryProvider _deviceInventoryProvider;
    private readonly IRecoveryPreparationStore _recoveryPreparationStore;
    private readonly ISystemEnvironmentProvider _systemEnvironmentProvider;
    private readonly ISystemRestoreInfoProvider _systemRestoreInfoProvider;

    public PhysicalPilotChecklistService(
        IExecutionEnvironmentDetector environmentDetector,
        IUpdatePlanService planService,
        IUpdatePreflightService preflightService,
        IDeviceInventoryProvider deviceInventoryProvider,
        IRecoveryPreparationStore recoveryPreparationStore,
        ISystemEnvironmentProvider systemEnvironmentProvider,
        ISystemRestoreInfoProvider systemRestoreInfoProvider)
    {
        _environmentDetector = environmentDetector;
        _planService = planService;
        _preflightService = preflightService;
        _deviceInventoryProvider = deviceInventoryProvider;
        _recoveryPreparationStore = recoveryPreparationStore;
        _systemEnvironmentProvider = systemEnvironmentProvider;
        _systemRestoreInfoProvider = systemRestoreInfoProvider;
    }

    public async Task<PilotChecklistResult> EvaluateAsync(
        Guid planId,
        CancellationToken cancellationToken = default)
    {
        var storedPlan = await _planService.GetPlanAsync(planId, cancellationToken)
            ?? throw new InvalidOperationException($"Plan '{planId}' was not found.");

        var environment = _environmentDetector.Detect();
        var capability = MutationCapabilityResolver.Resolve(environment);
        var items = new List<PilotChecklistItem>
        {
            EvaluateEnvironmentGate(environment, capability),
            EvaluateNotDisposableVm(environment),
        };

        var inventory = await _deviceInventoryProvider.ScanAsync(cancellationToken);
        var currentDevice = inventory.Devices.SingleOrDefault(device =>
            string.Equals(
                device.Snapshot.Identity.DeviceInstanceId,
                storedPlan.Plan.DeviceSnapshot.Identity.DeviceInstanceId,
                StringComparison.OrdinalIgnoreCase));

        items.Add(EvaluatePlanEligibility(storedPlan));
        items.Add(await EvaluatePlanStalenessAsync(storedPlan, currentDevice, cancellationToken));
        items.Add(EvaluatePreflight(storedPlan, currentDevice, capability));
        items.Add(await EvaluateRecoveryPreparationAsync(storedPlan.PlanId, cancellationToken));
        items.Add(await EvaluateSystemRestoreAsync(cancellationToken));
        items.Add(await EvaluatePendingRebootAsync(cancellationToken));

        var isReady = items.All(item => item.Status != PilotChecklistItemStatus.Blocked);
        var summary = isReady
            ? "Physical pilot checklist passed. Final confirmation is still required before any install."
            : "Physical pilot checklist is blocked. Resolve blocked items before continuing.";

        return new PilotChecklistResult(storedPlan.PlanId, items, isReady, summary);
    }

    private static PilotChecklistItem EvaluateEnvironmentGate(
        ExecutionEnvironmentInfo environment,
        IMutationCapability capability)
    {
        if (environment.IsDisposableVm)
        {
            return Blocked(
                "environment-gate",
                "Physical pilot markers",
                "Physical pilot cannot run on a detected disposable VM.");
        }

        if (!environment.MutationTestsEnabled)
        {
            return Blocked(
                "environment-gate",
                "Physical pilot markers",
                "Set TORTOISE_MUTATION_TESTS=1 before physical pilot work.");
        }

        if (!environment.PhysicalPilotExplicitlyAllowed)
        {
            return Blocked(
                "environment-gate",
                "Physical pilot markers",
                "Set TORTOISE_PHYSICAL_PILOT=1 before physical pilot work.");
        }

        return Passed(
            "environment-gate",
            "Physical pilot markers",
            "Physical pilot environment markers are present. Confirmation records readiness only and does not install drivers.");
    }

    private static PilotChecklistItem EvaluateNotDisposableVm(ExecutionEnvironmentInfo environment)
    {
        if (environment.IsDisposableVm)
        {
            return Blocked(
                "not-disposable-vm",
                "Physical machine scope",
                "Physical pilot cannot run on a detected disposable VM. Use tortoise vm install instead.");
        }

        return Passed(
            "not-disposable-vm",
            "Physical machine scope",
            "Host is not flagged as a disposable VM.");
    }

    private static PilotChecklistItem EvaluatePlanEligibility(StoredUpdatePlan storedPlan)
    {
        if (storedPlan.RiskLevel != DriverRiskLevel.Low
            || storedPlan.Classification != UpdateClassification.WindowsRecommended)
        {
            return Blocked(
                "plan-eligibility",
                "Low-risk plan only",
                "Physical pilot accepts only low-risk Windows-recommended updates.");
        }

        return Passed(
            "plan-eligibility",
            "Low-risk plan only",
            $"Plan classification is {storedPlan.Classification} with risk {storedPlan.RiskLevel}.");
    }

    private async Task<PilotChecklistItem> EvaluatePlanStalenessAsync(
        StoredUpdatePlan storedPlan,
        DeviceInventoryEntry? currentDevice,
        CancellationToken cancellationToken)
    {
        if (currentDevice is null)
        {
            return Blocked(
                "plan-staleness",
                "Plan freshness",
                "Target device is no longer present in the current inventory.");
        }

        var staleness = await _planService.EvaluateStalenessAsync(storedPlan.PlanId, cancellationToken);
        if (staleness.IsStale)
        {
            return Blocked(
                "plan-staleness",
                "Plan freshness",
                staleness.Explanation);
        }

        return Passed(
            "plan-staleness",
            "Plan freshness",
            "Frozen plan still matches the current device and driver state.");
    }

    private PilotChecklistItem EvaluatePreflight(
        StoredUpdatePlan storedPlan,
        DeviceInventoryEntry? currentDevice,
        IMutationCapability capability)
    {
        if (currentDevice is null)
        {
            return Blocked(
                "preflight",
                "Preflight checks",
                "Target device is not available for preflight.");
        }

        var preflight = _preflightService.RunPreflight(
            storedPlan,
            currentDevice,
            capability);

        if (preflight.IsBlocked)
        {
            var blocked = preflight.Checks.FirstOrDefault(check => check.Result == PreflightResult.Blocked);
            return Blocked(
                "preflight",
                "Preflight checks",
                blocked?.Message ?? "Preflight blocked the physical pilot plan.");
        }

        return Passed(
            "preflight",
            "Preflight checks",
            "Preflight checks passed for the physical pilot plan.");
    }

    private async Task<PilotChecklistItem> EvaluateRecoveryPreparationAsync(
        Guid planId,
        CancellationToken cancellationToken)
    {
        var preparations = await _recoveryPreparationStore.ListAsync(planId, cancellationToken);
        if (preparations.Count == 0)
        {
            return Blocked(
                "recovery-preparation",
                "Recovery preparation",
                "Run tortoise recover prepare before physical pilot confirmation.");
        }

        var latest = preparations[0];
        if (DateTimeOffset.UtcNow - latest.PreparedAtUtc > PhysicalPilotConstants.RecoveryPreparationMaxAge)
        {
            return Blocked(
                "recovery-preparation",
                "Recovery preparation",
                "Latest recovery preparation is stale. Run tortoise recover prepare again.");
        }

        if (!latest.ExportSucceeded && !latest.SystemRestoreEnabled)
        {
            return Blocked(
                "recovery-preparation",
                "Recovery preparation",
                "Recovery preparation exists but export did not succeed and System Restore is disabled.");
        }

        return Passed(
            "recovery-preparation",
            "Recovery preparation",
            $"Latest recovery preparation was created at {latest.PreparedAtUtc:G}.");
    }

    private async Task<PilotChecklistItem> EvaluateSystemRestoreAsync(CancellationToken cancellationToken)
    {
        var restoreInfo = await _systemRestoreInfoProvider.GetInfoAsync(cancellationToken);
        if (!restoreInfo.IsEnabled)
        {
            return Warning(
                "system-restore",
                "System Restore",
                restoreInfo.StatusMessage);
        }

        return Passed(
            "system-restore",
            "System Restore",
            restoreInfo.StatusMessage);
    }

    private async Task<PilotChecklistItem> EvaluatePendingRebootAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _systemEnvironmentProvider.CaptureAsync(cancellationToken);
        if (snapshot.PendingReboot)
        {
            return Blocked(
                "pending-reboot",
                "Pending reboot",
                "Complete or defer the pending reboot before starting a physical pilot install.");
        }

        return Passed(
            "pending-reboot",
            "Pending reboot",
            "No pending reboot was detected.");
    }

    private static PilotChecklistItem Passed(string id, string title, string detail) =>
        new(id, title, detail, PilotChecklistItemStatus.Passed);

    private static PilotChecklistItem Warning(string id, string title, string detail) =>
        new(id, title, detail, PilotChecklistItemStatus.Warning);

    private static PilotChecklistItem Blocked(string id, string title, string detail) =>
        new(id, title, detail, PilotChecklistItemStatus.Blocked);
}
