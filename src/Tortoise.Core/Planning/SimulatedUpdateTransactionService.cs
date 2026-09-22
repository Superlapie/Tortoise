using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Transactions;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Planning;

public sealed record SimulatedTransactionResult(
    UpdateTransactionRecord Transaction,
    UpdatePreflight Preflight,
    bool ReachedAwaitingConsent,
    string Summary);

public interface ISimulatedUpdateTransactionService
{
    Task<SimulatedTransactionResult> SimulateAsync(
        Guid planId,
        IMutationCapability mutationCapability,
        CancellationToken cancellationToken = default);
}

public interface IUpdatePlanStore
{
    Task SavePlansAsync(
        IReadOnlyList<StoredUpdatePlan> plans,
        CancellationToken cancellationToken = default);

    Task<StoredUpdatePlan?> GetPlanAsync(
        Guid planId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredUpdatePlan>> ListPlansAsync(
        long? scanSessionId = null,
        CancellationToken cancellationToken = default);
}

public interface IUpdateTransactionStore
{
    Task<UpdateTransactionRecord> SaveAsync(
        UpdateTransactionRecord transaction,
        CancellationToken cancellationToken = default);

    Task<UpdateTransactionRecord?> GetAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);

    Task<UpdateTransactionRecord?> GetLatestForPlanAsync(
        Guid planId,
        CancellationToken cancellationToken = default);
}

public sealed record UpdateTransactionRecord(
    UpdateTransaction Transaction,
    IReadOnlyList<OperationJournalEntry> Journal);

public sealed class SimulatedUpdateTransactionService : ISimulatedUpdateTransactionService
{
    private readonly IUpdatePlanService _planService;
    private readonly IUpdatePreflightService _preflightService;
    private readonly IUpdateTransactionStore _transactionStore;
    private readonly IDeviceInventoryProvider _deviceInventoryProvider;

    public SimulatedUpdateTransactionService(
        IUpdatePlanService planService,
        IUpdatePreflightService preflightService,
        IUpdateTransactionStore transactionStore,
        IDeviceInventoryProvider deviceInventoryProvider)
    {
        _planService = planService;
        _preflightService = preflightService;
        _transactionStore = transactionStore;
        _deviceInventoryProvider = deviceInventoryProvider;
    }

    public async Task<SimulatedTransactionResult> SimulateAsync(
        Guid planId,
        IMutationCapability mutationCapability,
        CancellationToken cancellationToken = default)
    {
        var storedPlan = await _planService.GetPlanAsync(planId, cancellationToken)
            ?? throw new InvalidOperationException($"Plan '{planId}' was not found.");

        var inventory = await _deviceInventoryProvider.ScanAsync(cancellationToken);
        var currentDevice = inventory.Devices.SingleOrDefault(device =>
                string.Equals(
                    device.Snapshot.Identity.DeviceInstanceId,
                    storedPlan.Plan.DeviceSnapshot.Identity.DeviceInstanceId,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Target device is no longer present in the current inventory.");

        var transactionId = Guid.NewGuid();
        var journal = new List<OperationJournalEntry>();
        var now = DateTimeOffset.UtcNow;

        var transaction = Advance(
            transactionId,
            storedPlan.PlanId,
            UpdateTransactionState.Discovered,
            UpdateTransactionState.Eligible,
            journal,
            "Recommendation converted to an eligible update transaction.",
            ref now);

        transaction = Advance(
            transaction.TransactionId,
            storedPlan.PlanId,
            transaction.State,
            UpdateTransactionState.Planned,
            journal,
            "Frozen plan attached to transaction.",
            ref now);

        transaction = Advance(
            transaction.TransactionId,
            storedPlan.PlanId,
            transaction.State,
            UpdateTransactionState.PreflightRunning,
            journal,
            "Preflight checks started.",
            ref now);

        var preflight = _preflightService.RunPreflight(
            storedPlan,
            currentDevice,
            mutationCapability,
            new PreflightContext(ForSimulation: true));

        if (preflight.IsBlocked)
        {
            transaction = Advance(
                transaction.TransactionId,
                storedPlan.PlanId,
                transaction.State,
                UpdateTransactionState.PreflightFailed,
                journal,
                "Preflight blocked the simulated workflow.",
                ref now);

            var failedRecord = new UpdateTransactionRecord(transaction, journal);
            await _transactionStore.SaveAsync(failedRecord, cancellationToken);

            return new SimulatedTransactionResult(
                failedRecord,
                preflight,
                ReachedAwaitingConsent: false,
                Summary: "Simulation stopped at preflight failure. No mutation was attempted.");
        }

        transaction = Advance(
            transaction.TransactionId,
            storedPlan.PlanId,
            transaction.State,
            UpdateTransactionState.PreflightPassed,
            journal,
            "Preflight checks passed for simulation.",
            ref now);

        transaction = Advance(
            transaction.TransactionId,
            storedPlan.PlanId,
            transaction.State,
            UpdateTransactionState.AwaitingConsent,
            journal,
            "Simulation halted before elevation. Mutation remains disabled.",
            ref now);

        var completedRecord = new UpdateTransactionRecord(transaction, journal);
        await _transactionStore.SaveAsync(completedRecord, cancellationToken);

        return new SimulatedTransactionResult(
            completedRecord,
            preflight,
            ReachedAwaitingConsent: true,
            Summary: "Simulation reached awaiting-consent without performing any driver mutation.");
    }

    private static UpdateTransaction Advance(
        Guid transactionId,
        Guid planId,
        UpdateTransactionState from,
        UpdateTransactionState to,
        IList<OperationJournalEntry> journal,
        string message,
        ref DateTimeOffset timestamp)
    {
        if (!UpdateTransactionTransitions.CanTransition(from, to))
        {
            throw new InvalidOperationException(
                $"Invalid simulated transition from '{from}' to '{to}'.");
        }

        timestamp = timestamp.AddMilliseconds(1);
        journal.Add(new OperationJournalEntry(transactionId, from, to, timestamp, message));
        return new UpdateTransaction(transactionId, planId, to, timestamp);
    }
}
