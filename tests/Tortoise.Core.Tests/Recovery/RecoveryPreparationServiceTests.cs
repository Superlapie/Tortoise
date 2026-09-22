using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Planning;
using Tortoise.Core.Recommendations;
using Tortoise.Core.Recovery;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Tests.Recovery;

public sealed class RecoveryPreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_captures_before_snapshot_and_records_disabled_export()
    {
        var storedPlan = CreateStoredPlan();
        var device = CreateDevice(new Version(1, 0));
        var store = new FakeRecoveryPreparationStore();
        var service = new RecoveryPreparationService(
            new FakeUpdatePlanService(storedPlan),
            new FakeDeviceInventoryProvider(device),
            new FakePackageInventoryProvider(),
            new DisabledExportService(),
            new FakeSystemEnvironmentProvider(),
            new FakeSystemRestoreInfoProvider(enabled: true),
            store);

        var record = await service.PrepareAsync(storedPlan.PlanId);

        Assert.Equal(storedPlan.PlanId, record.PlanId);
        Assert.Equal(device.Snapshot.Identity.DeviceInstanceId, record.Snapshot.BeforeDevice.Identity.DeviceInstanceId);
        Assert.False(record.ExportAttempt.ExportServiceEnabled);
        Assert.True(record.SystemRestore.IsEnabled);
        Assert.Single(store.Records);
    }

    [Fact]
    public async Task PrepareAsync_throws_when_plan_is_missing()
    {
        var service = new RecoveryPreparationService(
            new FakeUpdatePlanService(null),
            new FakeDeviceInventoryProvider(CreateDevice(new Version(1, 0))),
            new FakePackageInventoryProvider(),
            new DisabledExportService(),
            new FakeSystemEnvironmentProvider(),
            new FakeSystemRestoreInfoProvider(enabled: false),
            new FakeRecoveryPreparationStore());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PrepareAsync(Guid.NewGuid()));
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

    private sealed class FakeUpdatePlanService : IUpdatePlanService
    {
        private readonly StoredUpdatePlan? _plan;

        public FakeUpdatePlanService(StoredUpdatePlan? plan) => _plan = plan;

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
            Task.FromResult(_plan is not null && _plan.PlanId == planId ? _plan : null);

        public Task<IReadOnlyList<StoredUpdatePlan>> ListPlansAsync(
            long? scanSessionId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PlanStalenessResult> EvaluateStalenessAsync(
            Guid planId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeDeviceInventoryProvider : IDeviceInventoryProvider
    {
        private readonly DeviceInventoryEntry _device;

        public FakeDeviceInventoryProvider(DeviceInventoryEntry device) => _device = device;

        public Task<DeviceInventoryScanResult> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeviceInventoryScanResult([_device], [], DateTimeOffset.UtcNow));
    }

    private sealed class FakePackageInventoryProvider : IDriverPackageInventoryProvider
    {
        public Task<DriverPackageInventoryResult> ScanAsync(
            IReadOnlyList<DeviceInventoryEntry>? devices = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DriverPackageInventoryResult([], [], [], DateTimeOffset.UtcNow));
    }

    private sealed class DisabledExportService : IDriverPackageExportService
    {
        public bool IsExportEnabled => false;

        public string DisabledReason => "Export disabled for test.";

        public Task<DriverPackageExportResult> ExportAsync(
            DriverStorePackage package,
            string destinationDirectory,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DriverPackageExportResult(false, null, DisabledReason));
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
        public List<RecoveryPreparationRecord> Records { get; } = [];

        public Task SaveAsync(RecoveryPreparationRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task<RecoveryPreparationRecord?> GetAsync(
            Guid preparationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Records.FirstOrDefault(record => record.PreparationId == preparationId));

        public Task<IReadOnlyList<RecoveryPreparationSummary>> ListAsync(
            Guid? planId = null,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<RecoveryPreparationRecord> records = Records;
            if (planId is not null)
            {
                records = records.Where(record => record.PlanId == planId);
            }

            return Task.FromResult<IReadOnlyList<RecoveryPreparationSummary>>(
                records.Select(record => new RecoveryPreparationSummary(
                    record.PreparationId,
                    record.PlanId,
                    record.PreparedAtUtc,
                    record.Snapshot.BeforeDevice.Identity.FriendlyName,
                    record.ExportAttempt.Succeeded,
                    record.SystemRestore.IsEnabled)).ToList());
        }
    }
}
