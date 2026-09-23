using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.FaultInjection;
using Tortoise.Core.Planning;
using Tortoise.Core.Recommendations;
using Tortoise.Core.Transactions;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Tests.FaultInjection;

[Collection(nameof(FaultInjectionEnvironment))]
public sealed class FaultInjectionGateTests
{
    [Fact]
    public void IsEnabled_returns_false_without_marker()
    {
        using var scope = EnvironmentVariableScope.Set(TortoiseEnvironmentVariables.FaultInjection, null);
        Assert.False(FaultInjectionGate.IsEnabled());
    }

    [Fact]
    public void RequireEnabled_throws_without_marker()
    {
        using var scope = EnvironmentVariableScope.Set(TortoiseEnvironmentVariables.FaultInjection, null);
        Assert.Throws<InvalidOperationException>(FaultInjectionGate.RequireEnabled);
    }
}

[Collection(nameof(FaultInjectionEnvironment))]
public sealed class FaultInjectionWorkflowServiceTests
{
    [Theory]
    [InlineData(FaultInjectionScenario.ProcessCrash, UpdateTransactionState.Installing, true)]
    [InlineData(FaultInjectionScenario.UnexpectedReboot, UpdateTransactionState.AwaitingReboot, true)]
    [InlineData(FaultInjectionScenario.NetworkFailure, UpdateTransactionState.Failed, false)]
    [InlineData(FaultInjectionScenario.CandidateDisappeared, UpdateTransactionState.Failed, false)]
    public async Task RunScenarioAsync_records_expected_interrupt_state(
        FaultInjectionScenario scenario,
        UpdateTransactionState expectedState,
        bool requiresRecovery)
    {
        using var scope = EnvironmentVariableScope.Set(TortoiseEnvironmentVariables.FaultInjection, "1");

        var storedPlan = CreateStoredPlan();
        var device = CreateDevice(new Version(1, 0));
        var transactionStore = new FakeUpdateTransactionStore();
        var service = new FaultInjectionWorkflowService(
            new FakeUpdatePlanService(new FakeUpdatePlanStore(storedPlan)),
            new UpdatePreflightService(),
            transactionStore,
            new FakeDeviceInventoryProvider(device));

        var result = await service.RunScenarioAsync(storedPlan.PlanId, scenario);

        Assert.Equal(expectedState, result.Transaction.Transaction.State);
        Assert.Equal(scenario, result.InjectedFault.Scenario);
        Assert.Equal(requiresRecovery, result.InjectedFault.RequiresRecovery);
        Assert.Contains("[fault:", result.Transaction.Journal.Last().Message, StringComparison.Ordinal);
        Assert.NotNull(await transactionStore.GetAsync(result.Transaction.Transaction.TransactionId));
    }

    [Fact]
    public async Task RunScenarioAsync_denies_when_gate_disabled()
    {
        using var scope = EnvironmentVariableScope.Set(TortoiseEnvironmentVariables.FaultInjection, null);

        var storedPlan = CreateStoredPlan();
        var service = new FaultInjectionWorkflowService(
            new FakeUpdatePlanService(new FakeUpdatePlanStore(storedPlan)),
            new UpdatePreflightService(),
            new FakeUpdateTransactionStore(),
            new FakeDeviceInventoryProvider(CreateDevice(new Version(1, 0))));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RunScenarioAsync(storedPlan.PlanId, FaultInjectionScenario.ProcessCrash));
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

    private sealed class FakeUpdateTransactionStore : IUpdateTransactionStore
    {
        private UpdateTransactionRecord? _record;

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
            Task.FromResult(_record?.Transaction.TransactionId == transactionId ? _record : null);

        public Task<UpdateTransactionRecord?> GetLatestForPlanAsync(
            Guid planId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<UpdateTransactionRecord?>(_record?.Transaction.PlanId == planId ? _record : null);

        public Task<IReadOnlyList<UpdateTransactionRecord>> ListAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UpdateTransactionRecord>>(
                _record is null ? [] : [_record]);
    }

    private sealed class FakeDeviceInventoryProvider : IDeviceInventoryProvider
    {
        private readonly DeviceInventoryEntry _device;

        public FakeDeviceInventoryProvider(DeviceInventoryEntry device) => _device = device;

        public Task<DeviceInventoryScanResult> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeviceInventoryScanResult([_device], [], DateTimeOffset.UtcNow));
    }
}

public sealed class FaultReconciliationServiceTests
{
    [Theory]
    [InlineData(FaultInjectionScenario.ProcessCrash, FaultReconciliationAction.PrepareRecovery)]
    [InlineData(FaultInjectionScenario.UnexpectedReboot, FaultReconciliationAction.ResumeAfterReboot)]
    [InlineData(FaultInjectionScenario.NetworkFailure, FaultReconciliationAction.RescanAndRebuildPlan)]
    [InlineData(FaultInjectionScenario.CandidateDisappeared, FaultReconciliationAction.RescanAndRebuildPlan)]
    public async Task ReconcileAsync_recommends_expected_action(FaultInjectionScenario scenario, FaultReconciliationAction action)
    {
        var storedPlan = CreateStoredPlan();
        var transactionId = Guid.NewGuid();
        var record = new UpdateTransactionRecord(
            new UpdateTransaction(
                transactionId,
                storedPlan.PlanId,
                scenario is FaultInjectionScenario.ProcessCrash
                    ? UpdateTransactionState.Installing
                    : UpdateTransactionState.Failed,
                DateTimeOffset.UtcNow),
            [
                new OperationJournalEntry(
                    transactionId,
                    UpdateTransactionState.PreflightPassed,
                    UpdateTransactionState.Downloading,
                    DateTimeOffset.UtcNow,
                    FaultInjectionJournal.FormatMessage(scenario, "Simulated fault.")),
            ]);

        var service = new FaultReconciliationService(
            new FakeUpdateTransactionStore(record),
            new FakeUpdatePlanService(new FakeUpdatePlanStore(storedPlan)));

        var result = await service.ReconcileAsync(transactionId);

        Assert.Equal(action, result.RecommendedAction);
        Assert.Equal(scenario, result.InjectedScenario);
    }

    private static StoredUpdatePlan CreateStoredPlan()
    {
        var recommendation = new DeviceUpdateRecommendation(
            new DeviceInventoryEntry(
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
                new DeviceDriverBinding("Intel", new Version(1, 0), null, "intel.inf")),
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

    private sealed class FakeUpdateTransactionStore : IUpdateTransactionStore
    {
        private readonly UpdateTransactionRecord _record;

        public FakeUpdateTransactionStore(UpdateTransactionRecord record) => _record = record;

        public Task<UpdateTransactionRecord> SaveAsync(
            UpdateTransactionRecord transaction,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(transaction);

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

        public Task<IReadOnlyList<UpdateTransactionRecord>> ListAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UpdateTransactionRecord>>([_record]);
    }
}

internal sealed class EnvironmentVariableScope : IDisposable
{
    private readonly string _name;
    private readonly string? _originalValue;

    private EnvironmentVariableScope(string name, string? originalValue)
    {
        _name = name;
        _originalValue = originalValue;
    }

    public static EnvironmentVariableScope Set(string name, string? value)
    {
        var original = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new EnvironmentVariableScope(name, original);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(_name, _originalValue);
}
