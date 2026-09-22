using Tortoise.Core.Devices;
using Tortoise.Core.Policy;
using Tortoise.Core.Recommendations;
using Tortoise.Core.ScanSessions;

namespace Tortoise.Core.Planning;

public sealed class UpdatePlanService : IUpdatePlanService
{
    private readonly IScanSessionStore _scanSessionStore;
    private readonly IUpdatePlanStore _planStore;
    private readonly IUpdatePlanBuilder _planBuilder;
    private readonly IDeviceInventoryProvider _deviceInventoryProvider;
    private readonly IUpdatePlanPolicy _planPolicy;

    public UpdatePlanService(
        IScanSessionStore scanSessionStore,
        IUpdatePlanStore planStore,
        IUpdatePlanBuilder planBuilder,
        IDeviceInventoryProvider deviceInventoryProvider)
        : this(scanSessionStore, planStore, planBuilder, deviceInventoryProvider, new DefaultUpdatePlanPolicy())
    {
    }

    public UpdatePlanService(
        IScanSessionStore scanSessionStore,
        IUpdatePlanStore planStore,
        IUpdatePlanBuilder planBuilder,
        IDeviceInventoryProvider deviceInventoryProvider,
        IUpdatePlanPolicy planPolicy)
    {
        _scanSessionStore = scanSessionStore;
        _planStore = planStore;
        _planBuilder = planBuilder;
        _deviceInventoryProvider = deviceInventoryProvider;
        _planPolicy = planPolicy;
    }

    public async Task<IReadOnlyList<StoredUpdatePlan>> CreatePlansFromLatestScanAsync(
        UpdatePlanningOptions options,
        CancellationToken cancellationToken = default)
    {
        var latest = await _scanSessionStore.GetLatestAsync(cancellationToken)
            ?? throw new InvalidOperationException("No scan sessions are available for planning.");

        return await CreatePlansFromScanSessionAsync(latest.Id, options, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredUpdatePlan>> CreatePlansFromScanSessionAsync(
        long scanSessionId,
        UpdatePlanningOptions options,
        CancellationToken cancellationToken = default)
    {
        var session = await _scanSessionStore.GetByIdAsync(scanSessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Scan session {scanSessionId.ToString()} was not found.");

        var planningOptions = options with { ScanSessionId = scanSessionId };
        var plans = _planBuilder.BuildPlans(session.Result, planningOptions);
        await _planStore.SavePlansAsync(plans, cancellationToken);
        return plans;
    }

    public Task<StoredUpdatePlan?> GetPlanAsync(
        Guid planId,
        CancellationToken cancellationToken = default) =>
        _planStore.GetPlanAsync(planId, cancellationToken);

    public Task<IReadOnlyList<StoredUpdatePlan>> ListPlansAsync(
        long? scanSessionId = null,
        CancellationToken cancellationToken = default) =>
        _planStore.ListPlansAsync(scanSessionId, cancellationToken);

    public async Task<PlanStalenessResult> EvaluateStalenessAsync(
        Guid planId,
        CancellationToken cancellationToken = default)
    {
        var storedPlan = await _planStore.GetPlanAsync(planId, cancellationToken)
            ?? throw new InvalidOperationException($"Plan '{planId}' was not found.");

        var inventory = await _deviceInventoryProvider.ScanAsync(cancellationToken);
        var currentDevice = inventory.Devices.SingleOrDefault(device =>
            string.Equals(
                device.Snapshot.Identity.DeviceInstanceId,
                storedPlan.Plan.DeviceSnapshot.Identity.DeviceInstanceId,
                StringComparison.OrdinalIgnoreCase));

        if (currentDevice is null)
        {
            return new PlanStalenessResult(
                planId,
                true,
                "Target device is no longer present in the current inventory.");
        }

        var currentDriver = RecommendationPlanMapper.ToInstalledDriver(currentDevice);
        var isStale = _planPolicy.IsPlanStale(storedPlan.Plan, currentDevice.Snapshot, currentDriver);

        return new PlanStalenessResult(
            planId,
            isStale,
            isStale
                ? "The frozen plan no longer matches the installed driver or device state."
                : "The frozen plan still matches the current device and driver state.");
    }
}
