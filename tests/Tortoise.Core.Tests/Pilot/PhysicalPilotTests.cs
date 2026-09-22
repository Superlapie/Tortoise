using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Mutation;
using Tortoise.Core.Planning;
using Tortoise.Core.Pilot;
using Tortoise.Core.Recovery;
using Tortoise.Core.Recommendations;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Tests.Pilot;

public sealed class PhysicalPilotTests
{
    [Fact]
    public async Task Checklist_is_blocked_when_physical_pilot_markers_are_missing()
    {
        var storedPlan = CreateStoredPlan();
        var device = CreateDevice(new Version(1, 0));
        var service = CreateChecklistService(
            storedPlan,
            device,
            new ExecutionEnvironmentInfo(false, false, false, false));

        var result = await service.EvaluateAsync(storedPlan.PlanId);

        Assert.False(result.IsReady);
        Assert.Contains(result.Items, item =>
            item.Id == "environment-gate" && item.Status == PilotChecklistItemStatus.Blocked);
    }

    [Fact]
    public async Task Checklist_is_ready_when_all_requirements_are_met()
    {
        var storedPlan = CreateStoredPlan();
        var device = CreateDevice(new Version(1, 0));
        var service = CreateChecklistService(
            storedPlan,
            device,
            new ExecutionEnvironmentInfo(false, true, false, true),
            recoveryPreparations:
            [
                new RecoveryPreparationSummary(
                    Guid.NewGuid(),
                    storedPlan.PlanId,
                    DateTimeOffset.UtcNow,
                    device.Snapshot.Identity.FriendlyName,
                    ExportSucceeded: true,
                    SystemRestoreEnabled: true),
            ]);

        var result = await service.EvaluateAsync(storedPlan.PlanId);

        Assert.True(result.IsReady);
        Assert.DoesNotContain(result.Items, item => item.Status == PilotChecklistItemStatus.Blocked);
    }

    [Fact]
    public async Task ConfirmAsync_rejects_wrong_phrase()
    {
        var planId = Guid.NewGuid();
        var service = new PhysicalPilotConfirmationService(
            new FakeChecklistService(new PilotChecklistResult(planId, [], true, "Ready")),
            new FakePilotConfirmationStore());

        var result = await service.ConfirmAsync(
            new PhysicalPilotConfirmationRequest(
                planId,
                "WRONG PHRASE",
                PhysicalPilotConstants.RequiredAcknowledgementIds));

        Assert.False(result.Accepted);
        Assert.Contains("Confirmation phrase must exactly match", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmAsync_rejects_missing_acknowledgements()
    {
        var planId = Guid.NewGuid();
        var service = new PhysicalPilotConfirmationService(
            new FakeChecklistService(new PilotChecklistResult(planId, [], true, "Ready")),
            new FakePilotConfirmationStore());

        var result = await service.ConfirmAsync(
            new PhysicalPilotConfirmationRequest(
                planId,
                PhysicalPilotConstants.ConfirmationPhrase,
                ["recovery-docs-reviewed"]));

        Assert.False(result.Accepted);
        Assert.Contains("required acknowledgement", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmAsync_persists_record_when_checklist_is_ready()
    {
        var planId = Guid.NewGuid();
        var store = new FakePilotConfirmationStore();
        var service = new PhysicalPilotConfirmationService(
            new FakeChecklistService(new PilotChecklistResult(planId, [], true, "Ready")),
            store);

        var result = await service.ConfirmAsync(
            new PhysicalPilotConfirmationRequest(
                planId,
                PhysicalPilotConstants.ConfirmationPhrase,
                PhysicalPilotConstants.RequiredAcknowledgementIds));

        Assert.True(result.Accepted);
        Assert.Single(store.Records);
        Assert.Equal(planId, store.Records[0].PlanId);
    }

    private static PhysicalPilotChecklistService CreateChecklistService(
        StoredUpdatePlan storedPlan,
        DeviceInventoryEntry device,
        ExecutionEnvironmentInfo environment,
        IReadOnlyList<RecoveryPreparationSummary>? recoveryPreparations = null)
    {
        return new PhysicalPilotChecklistService(
            new StaticExecutionEnvironmentDetector(environment),
            new FakeUpdatePlanService(storedPlan),
            new FakePreflightService(),
            new FakeDeviceInventoryProvider(device),
            new FakeRecoveryPreparationStore(recoveryPreparations ?? []),
            new FakeSystemEnvironmentProvider(),
            new FakeSystemRestoreInfoProvider(enabled: true));
    }

    private static StoredUpdatePlan CreateStoredPlan()
    {
        var builder = new UpdatePlanBuilder();
        var device = CreateDevice(new Version(1, 0));
        var recommendation = new DeviceUpdateRecommendation(
            device,
            new WindowsUpdateCandidate(
                "update-id",
                1,
                "Intel Network Driver",
                null,
                "Intel",
                "Net",
                "Intel Adapter",
                new Version(2, 0),
                null,
                UpdateClassification.WindowsRecommended,
                false,
                false,
                false,
                false,
                []),
            UpdateClassification.WindowsRecommended,
            DriverRiskLevel.Low,
            "Recommended by Windows",
            "Explanation",
            DateTimeOffset.UtcNow);

        var scanResult = new RecommendationScanResult(
            [recommendation],
            [],
            new WindowsUpdatePolicyInfo(false, "Windows Update", "Update source: Windows Update."),
            [],
            DateTimeOffset.UtcNow);

        return builder.BuildPlans(scanResult, new UpdatePlanningOptions()).Single();
    }

    private static DeviceInventoryEntry CreateDevice(Version installedVersion)
    {
        return new DeviceInventoryEntry(
            new DeviceSnapshot(
                new DeviceIdentity(
                    "ROOT\\NET\\0001",
                    null,
                    Guid.NewGuid(),
                    "Net",
                    "Intel Adapter",
                    "Intel",
                    ["PCI\\VEN_8086&DEV_1234"],
                    [],
                    null,
                    null,
                    null,
                    null,
                    true),
                new DeviceHealth(DeviceHealthState.Healthy, null, null),
                DateTimeOffset.UtcNow),
            new DeviceDriverBinding("Intel", installedVersion, null, "intel.inf"));
    }

    private sealed class FakeChecklistService : IPhysicalPilotChecklistService
    {
        private readonly PilotChecklistResult _result;

        public FakeChecklistService(PilotChecklistResult result) => _result = result;

        public Task<PilotChecklistResult> EvaluateAsync(
            Guid planId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result with { PlanId = planId });
    }

    private sealed class FakePilotConfirmationStore : IPilotConfirmationStore
    {
        public List<PhysicalPilotConfirmationRecord> Records { get; } = [];

        public Task SaveAsync(
            PhysicalPilotConfirmationRecord record,
            CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task<PhysicalPilotConfirmationRecord?> GetLatestForPlanAsync(
            Guid planId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Records.LastOrDefault(record => record.PlanId == planId));
    }

    private sealed class FakeUpdatePlanService : IUpdatePlanService
    {
        private readonly StoredUpdatePlan _plan;

        public FakeUpdatePlanService(StoredUpdatePlan plan) => _plan = plan;

        public Task<IReadOnlyList<StoredUpdatePlan>> CreatePlansFromLatestScanAsync(
            UpdatePlanningOptions options,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StoredUpdatePlan>> CreatePlansFromScanSessionAsync(
            long scanSessionId,
            UpdatePlanningOptions options,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StoredUpdatePlan?> GetPlanAsync(Guid planId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_plan.PlanId == planId ? _plan : null);

        public Task<IReadOnlyList<StoredUpdatePlan>> ListPlansAsync(
            long? scanSessionId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PlanStalenessResult> EvaluateStalenessAsync(
            Guid planId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlanStalenessResult(planId, false, "Plan is fresh."));
    }

    private sealed class FakePreflightService : IUpdatePreflightService
    {
        public UpdatePreflight RunPreflight(
            StoredUpdatePlan storedPlan,
            DeviceInventoryEntry currentDevice,
            IMutationCapability mutationCapability,
            PreflightContext? context = null) =>
            new(
                storedPlan.PlanId,
                [new UpdatePreflightCheck("preflight", PreflightResult.Passed, "Passed")],
                false);
    }

    private sealed class FakeDeviceInventoryProvider : IDeviceInventoryProvider
    {
        private readonly DeviceInventoryEntry _device;

        public FakeDeviceInventoryProvider(DeviceInventoryEntry device) => _device = device;

        public Task<DeviceInventoryScanResult> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeviceInventoryScanResult([_device], [], DateTimeOffset.UtcNow));
    }

    private sealed class FakeSystemEnvironmentProvider : ISystemEnvironmentProvider
    {
        public Task<SystemSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SystemSnapshot("Windows Test", "X64", false, DateTimeOffset.UtcNow));
    }

    private sealed class FakeSystemRestoreInfoProvider : ISystemRestoreInfoProvider
    {
        private readonly bool _enabled;

        public FakeSystemRestoreInfoProvider(bool enabled) => _enabled = enabled;

        public Task<SystemRestoreInfo> GetInfoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SystemRestoreInfo(_enabled, _enabled ? "Enabled" : "Disabled"));
    }

    private sealed class FakeRecoveryPreparationStore : IRecoveryPreparationStore
    {
        private readonly IReadOnlyList<RecoveryPreparationSummary> _summaries;

        public FakeRecoveryPreparationStore(IReadOnlyList<RecoveryPreparationSummary> summaries) =>
            _summaries = summaries;

        public Task SaveAsync(RecoveryPreparationRecord record, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RecoveryPreparationRecord?> GetAsync(
            Guid preparationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RecoveryPreparationSummary>> ListAsync(
            Guid? planId = null,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<RecoveryPreparationSummary> summaries = _summaries;
            if (planId is not null)
            {
                summaries = summaries.Where(summary => summary.PlanId == planId);
            }

            return Task.FromResult<IReadOnlyList<RecoveryPreparationSummary>>(summaries.ToList());
        }
    }
}
