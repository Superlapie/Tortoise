using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Installation;
using Tortoise.Core.Mutation;
using Tortoise.Core.Planning;
using Tortoise.Core.Recommendations;
using Tortoise.Core.Transactions;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Tests.Installation;

public sealed class RealPostInstallVerificationServiceTests
{
    private readonly RealPostInstallVerificationService _service = new();

    [Fact]
    public void VerifyPostInstall_passes_when_installed_version_matches_plan()
    {
        var storedPlan = CreateStoredPlan(new Version(2, 0));
        var before = CreateDevice(new Version(1, 0));
        var after = CreateDevice(new Version(2, 0));

        var verification = _service.VerifyPostInstall(storedPlan, before, after, Guid.NewGuid());

        Assert.Equal(UpdateVerificationResult.Verified, verification.Result);
    }

    [Fact]
    public void VerifyPostInstall_fails_when_version_does_not_match()
    {
        var storedPlan = CreateStoredPlan(new Version(2, 0));
        var before = CreateDevice(new Version(1, 0));
        var after = CreateDevice(new Version(1, 0));

        var verification = _service.VerifyPostInstall(storedPlan, before, after, Guid.NewGuid());

        Assert.Equal(UpdateVerificationResult.Failed, verification.Result);
    }

    private static StoredUpdatePlan CreateStoredPlan(Version proposedVersion)
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
                proposedVersion,
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
}

public sealed class VmDriverInstallServiceTests
{
    [Fact]
    public async Task InstallAsync_denies_when_mutation_is_not_vm_enabled()
    {
        var service = CreateService(
            new ExecutionEnvironmentInfo(false, false, false),
            new FakeBrokerDriverInstallClient(new BrokerDriverInstallResult(true, "ok", 2, false)));

        await Assert.ThrowsAsync<MutationDeniedException>(() =>
            service.InstallAsync(Guid.NewGuid(), new VmDriverInstallOptions(BrokerOptions: new BrokerPlanValidationOptions(1, "test-capability"))));
    }

    [Fact]
    public async Task InstallAsync_blocks_non_low_risk_plans()
    {
        var storedPlan = CreateStoredPlan(DriverRiskLevel.Moderate);
        var service = CreateService(
            new ExecutionEnvironmentInfo(true, true, true),
            new FakeBrokerDriverInstallClient(new BrokerDriverInstallResult(true, "ok", 2, false)),
            storedPlan);

        var result = await service.InstallAsync(
            storedPlan.PlanId,
            new VmDriverInstallOptions(
                RequireBrokerValidation: false,
                BrokerOptions: new BrokerPlanValidationOptions(1, "test-capability")));

        Assert.False(result.CompletedSuccessfully);
        Assert.Contains("not eligible", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallAsync_returns_incomplete_when_broker_requires_reboot()
    {
        var storedPlan = CreateStoredPlan(DriverRiskLevel.Low);
        var device = CreateDevice(new Version(1, 0));
        var service = CreateService(
            new ExecutionEnvironmentInfo(true, true, true),
            new FakeBrokerDriverInstallClient(new BrokerDriverInstallResult(true, "installed", 2, true)),
            storedPlan,
            device,
            afterInstallVersion: new Version(1, 0));

        var result = await service.InstallAsync(
            storedPlan.PlanId,
            new VmDriverInstallOptions(
                RequireBrokerValidation: false,
                BrokerOptions: new BrokerPlanValidationOptions(1, "test-capability")));

        Assert.False(result.CompletedSuccessfully);
        Assert.Equal(UpdateVerificationResult.InstalledRestartRequired, result.PostInstallVerification.Result);
    }

    [Fact]
    public async Task InstallAsync_completes_when_broker_install_and_verification_succeed()
    {
        var storedPlan = CreateStoredPlan(DriverRiskLevel.Low);
        var device = CreateDevice(new Version(1, 0));
        var service = CreateService(
            new ExecutionEnvironmentInfo(true, true, true),
            new FakeBrokerDriverInstallClient(new BrokerDriverInstallResult(true, "installed", 2, false)),
            storedPlan,
            device,
            afterInstallVersion: new Version(2, 0));

        var result = await service.InstallAsync(
            storedPlan.PlanId,
            new VmDriverInstallOptions(
                RequireBrokerValidation: false,
                BrokerOptions: new BrokerPlanValidationOptions(1, "test-capability")));

        Assert.True(result.CompletedSuccessfully);
        Assert.Equal(UpdateVerificationResult.Verified, result.PostInstallVerification.Result);
    }

    private static VmDriverInstallService CreateService(
        ExecutionEnvironmentInfo environment,
        IBrokerDriverInstallClient brokerInstallClient,
        StoredUpdatePlan? storedPlan = null,
        DeviceInventoryEntry? device = null,
        Version? afterInstallVersion = null)
    {
        storedPlan ??= CreateStoredPlan(DriverRiskLevel.Low);
        device ??= CreateDevice(new Version(1, 0));
        afterInstallVersion ??= device.InstalledDriver!.DriverVersion;

        var planStore = new FakeUpdatePlanStore(storedPlan);
        var beforeDevice = device;
        var afterDevice = CreateDevice(afterInstallVersion ?? new Version(2, 0));

        return new VmDriverInstallService(
            new StaticExecutionEnvironmentDetector(environment),
            new FakeUpdatePlanService(planStore),
            new UpdatePreflightService(),
            new ToggleDeviceInventoryProvider(beforeDevice, afterDevice),
            new FakeUpdateTransactionStore(),
            new RealPostInstallVerificationService(),
            brokerPlanValidationClient: null,
            brokerDriverInstallClient: brokerInstallClient);
    }

    private static StoredUpdatePlan CreateStoredPlan(DriverRiskLevel riskLevel)
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
            riskLevel,
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
        public Task<UpdateTransactionRecord> SaveAsync(
            UpdateTransactionRecord transaction,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(transaction);

        public Task<UpdateTransactionRecord?> GetAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<UpdateTransactionRecord?>(null);

        public Task<UpdateTransactionRecord?> GetLatestForPlanAsync(
            Guid planId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<UpdateTransactionRecord?>(null);
    }

    private sealed class FakeBrokerDriverInstallClient : IBrokerDriverInstallClient
    {
        private readonly BrokerDriverInstallResult _result;

        public FakeBrokerDriverInstallClient(BrokerDriverInstallResult result) => _result = result;

        public Task<BrokerDriverInstallResult> InstallDriverAsync(
            Guid planId,
            string planHash,
            string updateId,
            int revision,
            BrokerPlanValidationOptions options,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class FakeUpdatePlanStore : IUpdatePlanStore
    {
        private readonly StoredUpdatePlan _plan;

        public FakeUpdatePlanStore(StoredUpdatePlan plan) => _plan = plan;

        public Task SavePlansAsync(IReadOnlyList<StoredUpdatePlan> plans, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<StoredUpdatePlan?> GetPlanAsync(Guid planId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StoredUpdatePlan?>(planId == _plan.PlanId ? _plan : null);

        public Task<IReadOnlyList<StoredUpdatePlan>> ListPlansAsync(
            long? scanSessionId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredUpdatePlan>>([_plan]);
    }

    private sealed class FakeUpdatePlanService : IUpdatePlanService
    {
        private readonly FakeUpdatePlanStore _store;

        public FakeUpdatePlanService(FakeUpdatePlanStore store) => _store = store;

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
            _store.GetPlanAsync(planId, cancellationToken);

        public Task<IReadOnlyList<StoredUpdatePlan>> ListPlansAsync(
            long? scanSessionId = null,
            CancellationToken cancellationToken = default) =>
            _store.ListPlansAsync(scanSessionId, cancellationToken);

        public Task<PlanStalenessResult> EvaluateStalenessAsync(
            Guid planId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
            var device = _scanCount <= 1 ? _before : _after;
            return Task.FromResult(new DeviceInventoryScanResult([device], [], DateTimeOffset.UtcNow));
        }
    }
}
