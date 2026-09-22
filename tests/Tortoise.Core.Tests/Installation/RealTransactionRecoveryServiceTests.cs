using Tortoise.Core.Devices;
using Tortoise.Core.Installation;
using Tortoise.Core.Planning;
using Tortoise.Core.Recommendations;
using Tortoise.Core.Transactions;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Tests.Installation;

public sealed class RealTransactionRecoveryServiceTests
{
    [Fact]
    public async Task ResumeAfterRebootAsync_completes_when_post_reboot_verification_passes()
    {
        var storedPlan = CreateStoredPlan();
        var transactionId = Guid.NewGuid();
        var record = new UpdateTransactionRecord(
            new UpdateTransaction(
                transactionId,
                storedPlan.PlanId,
                UpdateTransactionState.AwaitingReboot,
                DateTimeOffset.UtcNow),
            [
                new OperationJournalEntry(
                    transactionId,
                    UpdateTransactionState.InstallReturned,
                    UpdateTransactionState.AwaitingReboot,
                    DateTimeOffset.UtcNow,
                    "Awaiting reboot."),
            ]);

        var service = new RealTransactionRecoveryService(
            new FakeUpdateTransactionStore(record),
            new FakeUpdatePlanService(storedPlan),
            new StaticDeviceInventoryProvider(CreateDevice(new Version(2, 0))),
            new RealPostInstallVerificationService());

        var result = await service.ResumeAfterRebootAsync(transactionId);

        Assert.True(result.CompletedSuccessfully);
        Assert.Equal(UpdateTransactionState.Completed, result.FinalState);
    }

    private static StoredUpdatePlan CreateStoredPlan()
    {
        var recommendation = new DeviceUpdateRecommendation(
            CreateDevice(new Version(1, 0)),
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

        return UpdatePlanBuilder.CreateStoredPlan(recommendation, scanSessionId: 1);
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

    private sealed class FakeUpdateTransactionStore : IUpdateTransactionStore
    {
        private UpdateTransactionRecord _record;

        public FakeUpdateTransactionStore(UpdateTransactionRecord record) => _record = record;

        public Task<UpdateTransactionRecord> SaveAsync(
            UpdateTransactionRecord transaction,
            CancellationToken cancellationToken = default)
        {
            _record = transaction;
            return Task.FromResult(transaction);
        }

        public Task<UpdateTransactionRecord?> GetAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<UpdateTransactionRecord?>(
                transactionId == _record.Transaction.TransactionId ? _record : null);

        public Task<UpdateTransactionRecord?> GetLatestForPlanAsync(
            Guid planId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<UpdateTransactionRecord?>(
                planId == _record.Transaction.PlanId ? _record : null);
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
            Task.FromResult<StoredUpdatePlan?>(planId == _plan.PlanId ? _plan : null);

        public Task<IReadOnlyList<StoredUpdatePlan>> ListPlansAsync(
            long? scanSessionId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredUpdatePlan>>([_plan]);

        public Task<PlanStalenessResult> EvaluateStalenessAsync(
            Guid planId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StaticDeviceInventoryProvider : IDeviceInventoryProvider
    {
        private readonly DeviceInventoryEntry _device;

        public StaticDeviceInventoryProvider(DeviceInventoryEntry device) => _device = device;

        public Task<DeviceInventoryScanResult> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeviceInventoryScanResult([_device], [], DateTimeOffset.UtcNow));
    }

    private sealed class ToggleDeviceInventoryProvider : IDeviceInventoryProvider
    {
        private readonly DeviceInventoryEntry _before;
        private readonly DeviceInventoryEntry _after;
        private int _scanCount;

        public ToggleDeviceInventoryProvider(DeviceInventoryEntry before, DeviceInventoryEntry after)
        {
            _before = before;
            _after = after;
        }

        public Task<DeviceInventoryScanResult> ScanAsync(CancellationToken cancellationToken = default)
        {
            _scanCount++;
            return Task.FromResult(new DeviceInventoryScanResult(
                [_scanCount <= 1 ? _before : _after],
                [],
                DateTimeOffset.UtcNow));
        }
    }
}
