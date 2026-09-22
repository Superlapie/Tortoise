using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Planning;
using Tortoise.Core.Recommendations;
using Tortoise.Core.Transactions;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Tests.Planning;

public sealed class UpdatePlanBuilderTests
{
    private readonly UpdatePlanBuilder _builder = new();

    [Fact]
    public void BuildPlans_creates_frozen_plan_for_actionable_recommendation()
    {
        var recommendation = CreateRecommendation(UpdateClassification.WindowsRecommended);
        var scanResult = new RecommendationScanResult(
            [recommendation],
            [],
            new WindowsUpdatePolicyInfo(false, "Windows Update", "Update source: Windows Update."),
            [],
            DateTimeOffset.UtcNow);

        var plans = _builder.BuildPlans(scanResult, new UpdatePlanningOptions());

        var storedPlan = Assert.Single(plans);
        Assert.True(storedPlan.IsFrozen);
        Assert.Equal(UpdateClassification.WindowsRecommended, storedPlan.Classification);
        Assert.NotEmpty(storedPlan.Plan.PlanHash);
    }

    [Fact]
    public void BuildPlans_skips_current_and_restricted_recommendations()
    {
        var current = CreateRecommendation(UpdateClassification.Current, includeUpdate: false);
        var restricted = CreateRecommendation(UpdateClassification.Restricted);
        var scanResult = new RecommendationScanResult(
            [current, restricted],
            [],
            new WindowsUpdatePolicyInfo(false, "Windows Update", "Update source: Windows Update."),
            [],
            DateTimeOffset.UtcNow);

        Assert.Empty(_builder.BuildPlans(scanResult, new UpdatePlanningOptions()));
    }

    private static DeviceUpdateRecommendation CreateRecommendation(
        UpdateClassification classification,
        bool includeUpdate = true)
    {
        var device = new DeviceInventoryEntry(
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
            new DeviceDriverBinding("Intel", new Version(1, 0), null, "intel.inf"));

        var update = includeUpdate
            ? new WindowsUpdateCandidate(
                "update-id",
                1,
                "Intel Network Driver",
                null,
                "Intel",
                "Net",
                "Intel Adapter",
                new Version(2, 0),
                null,
                classification,
                false,
                false,
                false,
                false,
                [])
            : null;

        return new DeviceUpdateRecommendation(
            device,
            update,
            classification,
            DriverRiskLevel.Low,
            "Summary",
            "Explanation",
            DateTimeOffset.UtcNow);
    }
}

public sealed class UpdatePreflightServiceTests
{
    private readonly UpdatePreflightService _service = new();

    [Fact]
    public void RunPreflight_blocks_when_mutation_is_disabled()
    {
        var storedPlan = CreateStoredPlan();
        var device = CreateDevice(new Version(1, 0));

        var preflight = _service.RunPreflight(
            storedPlan,
            device,
            MutationCapability.ReadOnly);

        Assert.True(preflight.IsBlocked);
        Assert.Contains(
            preflight.Checks,
            check => check.Name == "mutation-capability" && check.Result == PreflightResult.Blocked);
    }

    [Fact]
    public void RunPreflight_warns_for_simulation_when_mutation_is_disabled()
    {
        var storedPlan = CreateStoredPlan();
        var device = CreateDevice(new Version(1, 0));

        var preflight = _service.RunPreflight(
            storedPlan,
            device,
            MutationCapability.ReadOnly,
            new PreflightContext(ForSimulation: true));

        Assert.False(preflight.IsBlocked);
        Assert.Contains(
            preflight.Checks,
            check => check.Name == "mutation-capability" && check.Result == PreflightResult.Warning);
    }

    [Fact]
    public void RunPreflight_blocks_when_plan_is_stale()
    {
        var storedPlan = CreateStoredPlan();
        var device = CreateDevice(new Version(9, 0));

        var preflight = _service.RunPreflight(
            storedPlan,
            device,
            MutationCapability.ReadOnly,
            new PreflightContext(ForSimulation: true));

        Assert.True(preflight.IsBlocked);
        Assert.Contains(
            preflight.Checks,
            check => check.Name == "plan-staleness" && check.Result == PreflightResult.Blocked);
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
}

public sealed class SimulatedUpdateTransactionServiceTests
{
    [Fact]
    public async Task SimulateAsync_reaches_awaiting_consent_without_mutation()
    {
        var storedPlan = CreateStoredPlan();
        var device = CreateDevice(new Version(1, 0));
        var planStore = new FakeUpdatePlanStore(storedPlan);
        var transactionStore = new FakeUpdateTransactionStore();
        var service = CreateSimulatedTransactionService(
            new FakeUpdatePlanService(planStore),
            transactionStore,
            new FakeDeviceInventoryProvider(device));

        var result = await service.SimulateAsync(storedPlan.PlanId, MutationCapability.ReadOnly);

        Assert.True(result.ReachedAwaitingConsent);
        Assert.Equal(UpdateTransactionState.AwaitingConsent, result.Transaction.Transaction.State);
        Assert.True(result.Transaction.Journal.Count >= 4);
        Assert.Contains(
            result.Transaction.Journal,
            entry => entry.ToState == UpdateTransactionState.AwaitingConsent);
    }

    internal static StoredUpdatePlan CreateStoredPlan()
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

    internal static DeviceInventoryEntry CreateDevice(Version installedVersion)
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

    internal static SimulatedUpdateTransactionService CreateSimulatedTransactionService(
        IUpdatePlanService planService,
        IUpdateTransactionStore transactionStore,
        IDeviceInventoryProvider deviceInventoryProvider,
        IBrokerPlanValidationClient? brokerPlanValidationClient = null) =>
        new(
            planService,
            new UpdatePreflightService(),
            transactionStore,
            deviceInventoryProvider,
            new SimulatedUpdatePackageVerificationService(),
            new SimulatedPostInstallVerificationService(),
            brokerPlanValidationClient);

    internal sealed class FakeUpdatePlanStore : IUpdatePlanStore
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

    internal sealed class FakeUpdatePlanService : IUpdatePlanService
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

    internal sealed class FakeUpdateTransactionStore : IUpdateTransactionStore
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

    internal sealed class FakeDeviceInventoryProvider : IDeviceInventoryProvider
    {
        private readonly DeviceInventoryEntry _device;

        public FakeDeviceInventoryProvider(DeviceInventoryEntry device) => _device = device;

        public Task<DeviceInventoryScanResult> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeviceInventoryScanResult([_device], [], DateTimeOffset.UtcNow));
    }
}

public sealed class SimulatedFullWorkflowTests
{
    [Fact]
    public async Task SimulateFullWorkflowAsync_completes_without_mutation()
    {
        var storedPlan = SimulatedUpdateTransactionServiceTests.CreateStoredPlan();
        var device = SimulatedUpdateTransactionServiceTests.CreateDevice(new Version(1, 0));
        var planStore = new SimulatedUpdateTransactionServiceTests.FakeUpdatePlanStore(storedPlan);
        var transactionStore = new SimulatedUpdateTransactionServiceTests.FakeUpdateTransactionStore();
        var service = SimulatedUpdateTransactionServiceTests.CreateSimulatedTransactionService(
            new SimulatedUpdateTransactionServiceTests.FakeUpdatePlanService(planStore),
            transactionStore,
            new SimulatedUpdateTransactionServiceTests.FakeDeviceInventoryProvider(device));

        var result = await service.SimulateFullWorkflowAsync(
            storedPlan.PlanId,
            MutationCapability.Simulated);

        Assert.True(result.CompletedSuccessfully);
        Assert.Equal(UpdateTransactionState.Completed, result.Transaction.Transaction.State);
        Assert.Equal(UpdateVerificationResult.Verified, result.PackageVerification.Result);
        Assert.Equal(UpdateVerificationResult.Verified, result.PostInstallVerification.Result);
        Assert.Contains(
            result.Transaction.Journal,
            entry => entry.ToState == UpdateTransactionState.Verified);
        Assert.Contains(
            result.Transaction.Journal,
            entry => entry.ToState == UpdateTransactionState.Completed);
    }

    [Fact]
    public async Task SimulateFullWorkflowAsync_stops_when_broker_validation_fails()
    {
        var storedPlan = SimulatedUpdateTransactionServiceTests.CreateStoredPlan();
        var device = SimulatedUpdateTransactionServiceTests.CreateDevice(new Version(1, 0));
        var planStore = new SimulatedUpdateTransactionServiceTests.FakeUpdatePlanStore(storedPlan);
        var transactionStore = new SimulatedUpdateTransactionServiceTests.FakeUpdateTransactionStore();
        var brokerClient = new FakeBrokerPlanValidationClient(
            new BrokerPlanValidationResult(false, "Broker rejected plan.", "PlanValidationFailed"));
        var service = SimulatedUpdateTransactionServiceTests.CreateSimulatedTransactionService(
            new SimulatedUpdateTransactionServiceTests.FakeUpdatePlanService(planStore),
            transactionStore,
            new SimulatedUpdateTransactionServiceTests.FakeDeviceInventoryProvider(device),
            brokerClient);

        var result = await service.SimulateFullWorkflowAsync(
            storedPlan.PlanId,
            MutationCapability.Simulated,
            new SimulatedWorkflowExecutionOptions(
                RequireBrokerValidation: true,
                BrokerOptions: new BrokerPlanValidationOptions(SessionId: 42, CapabilityToken: "test-capability")));

        Assert.False(result.CompletedSuccessfully);
        Assert.Equal(UpdateTransactionState.Failed, result.Transaction.Transaction.State);
        Assert.NotNull(result.BrokerValidation);
        Assert.False(result.BrokerValidation!.Succeeded);
    }

    private sealed class FakeBrokerPlanValidationClient : IBrokerPlanValidationClient
    {
        private readonly BrokerPlanValidationResult _result;

        public FakeBrokerPlanValidationClient(BrokerPlanValidationResult result) => _result = result;

        public Task<BrokerPlanValidationResult> ValidatePlanAsync(
            Guid planId,
            string planHash,
            BrokerPlanValidationOptions options,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }
}

public sealed class SimulatedPackageVerificationTests
{
    [Fact]
    public void VerifyPackage_passes_for_valid_frozen_plan()
    {
        var storedPlan = SimulatedUpdateTransactionServiceTests.CreateStoredPlan();
        var service = new SimulatedUpdatePackageVerificationService();

        var verification = service.VerifyPackage(storedPlan, Guid.NewGuid());

        Assert.Equal(UpdateVerificationResult.Verified, verification.Result);
    }
}

public sealed class SimulatedMutationWorkflowServiceTests
{
    [Fact]
    public async Task RunAsync_delegates_to_full_workflow()
    {
        var storedPlan = SimulatedUpdateTransactionServiceTests.CreateStoredPlan();
        var device = SimulatedUpdateTransactionServiceTests.CreateDevice(new Version(1, 0));
        var planStore = new SimulatedUpdateTransactionServiceTests.FakeUpdatePlanStore(storedPlan);
        var transactionStore = new SimulatedUpdateTransactionServiceTests.FakeUpdateTransactionStore();
        var transactionService = SimulatedUpdateTransactionServiceTests.CreateSimulatedTransactionService(
            new SimulatedUpdateTransactionServiceTests.FakeUpdatePlanService(planStore),
            transactionStore,
            new SimulatedUpdateTransactionServiceTests.FakeDeviceInventoryProvider(device));
        var workflow = new SimulatedMutationWorkflowService(transactionService);

        var result = await workflow.RunAsync(
            storedPlan.PlanId,
            new SimulatedMutationWorkflowOptions());

        Assert.True(result.CompletedSuccessfully);
        Assert.Equal(UpdateTransactionState.Completed, result.Transaction.Transaction.State);
    }
}
