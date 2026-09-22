using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Planning;
using Tortoise.Core.Transactions;
using Tortoise.Core.Updates;

namespace Tortoise.Core.FaultInjection;

public sealed class FaultInjectionWorkflowService : IFaultInjectionWorkflowService
{
    private readonly IUpdatePlanService _planService;
    private readonly IUpdatePreflightService _preflightService;
    private readonly IUpdateTransactionStore _transactionStore;
    private readonly IDeviceInventoryProvider _deviceInventoryProvider;

    public FaultInjectionWorkflowService(
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

    public async Task<FaultInjectionWorkflowResult> RunScenarioAsync(
        Guid planId,
        FaultInjectionScenario scenario,
        CancellationToken cancellationToken = default)
    {
        FaultInjectionGate.RequireEnabled();

        var definition = FaultInjectionScenarioCatalog.Get(scenario);
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
        var timestamp = DateTimeOffset.UtcNow;

        Advance(
            transactionId,
            storedPlan.PlanId,
            UpdateTransactionState.Discovered,
            UpdateTransactionState.Eligible,
            journal,
            "Recommendation converted to an eligible update transaction.",
            ref timestamp);

        Advance(
            transactionId,
            storedPlan.PlanId,
            UpdateTransactionState.Eligible,
            UpdateTransactionState.Planned,
            journal,
            "Frozen plan attached to transaction.",
            ref timestamp);

        Advance(
            transactionId,
            storedPlan.PlanId,
            UpdateTransactionState.Planned,
            UpdateTransactionState.PreflightRunning,
            journal,
            "Preflight checks started.",
            ref timestamp);

        var preflight = _preflightService.RunPreflight(
            storedPlan,
            currentDevice,
            MutationCapability.Simulated,
            new PreflightContext(ForSimulation: true));

        if (preflight.IsBlocked)
        {
            Advance(
                transactionId,
                storedPlan.PlanId,
                UpdateTransactionState.PreflightRunning,
                UpdateTransactionState.PreflightFailed,
                journal,
                "Preflight blocked the fault injection workflow.",
                ref timestamp);

            var blockedRecord = new UpdateTransactionRecord(
                new UpdateTransaction(transactionId, storedPlan.PlanId, UpdateTransactionState.PreflightFailed, timestamp),
                journal);

            await _transactionStore.SaveAsync(blockedRecord, cancellationToken);

            var blockedFault = new InjectedFaultResult(
                scenario,
                definition.Checkpoint,
                UpdateTransactionState.PreflightFailed,
                RequiresRecovery: false,
                "Fault scenario stopped at preflight failure.",
                "Rescan the device and rebuild the plan before retrying fault injection.");

            return new FaultInjectionWorkflowResult(
                blockedRecord,
                preflight,
                blockedFault,
                Summary: blockedFault.Message);
        }

        Advance(
            transactionId,
            storedPlan.PlanId,
            UpdateTransactionState.PreflightRunning,
            UpdateTransactionState.PreflightPassed,
            journal,
            "Preflight checks passed for fault injection.",
            ref timestamp);

        var interruptedState = RunScenarioTail(
            scenario,
            definition,
            storedPlan,
            transactionId,
            journal,
            ref timestamp);

        var injectedFault = CreateInjectedFault(scenario, definition, interruptedState);
        journal.Add(new OperationJournalEntry(
            transactionId,
            interruptedState,
            interruptedState,
            timestamp.AddMilliseconds(1),
            FaultInjectionJournal.FormatMessage(scenario, injectedFault.Message)));

        var transaction = new UpdateTransaction(transactionId, storedPlan.PlanId, interruptedState, timestamp);
        var record = new UpdateTransactionRecord(transaction, journal);
        await _transactionStore.SaveAsync(record, cancellationToken);

        return new FaultInjectionWorkflowResult(
            record,
            preflight,
            injectedFault,
            Summary: injectedFault.Message);
    }

    private static UpdateTransactionState RunScenarioTail(
        FaultInjectionScenario scenario,
        FaultScenarioDefinition definition,
        StoredUpdatePlan storedPlan,
        Guid transactionId,
        List<OperationJournalEntry> journal,
        ref DateTimeOffset timestamp)
    {
        return scenario switch
        {
            FaultInjectionScenario.NetworkFailure => FailDuringDownload(
                transactionId,
                storedPlan.PlanId,
                journal,
                ref timestamp,
                "Simulated Windows Update network failure during download."),
            FaultInjectionScenario.CandidateDisappeared => FailDuringDownload(
                transactionId,
                storedPlan.PlanId,
                journal,
                ref timestamp,
                "Simulated Windows Update candidate disappearance during download."),
            FaultInjectionScenario.ProcessCrash => InterruptDuringInstall(
                transactionId,
                storedPlan.PlanId,
                journal,
                ref timestamp),
            FaultInjectionScenario.UnexpectedReboot => InterruptAfterInstall(
                transactionId,
                storedPlan.PlanId,
                journal,
                ref timestamp),
            _ => throw new InvalidOperationException($"Unsupported fault scenario '{scenario}'."),
        };
    }

    private static UpdateTransactionState FailDuringDownload(
        Guid transactionId,
        Guid planId,
        List<OperationJournalEntry> journal,
        ref DateTimeOffset timestamp,
        string message)
    {
        Advance(
            transactionId,
            planId,
            UpdateTransactionState.PreflightPassed,
            UpdateTransactionState.Downloading,
            journal,
            "Simulated package download started.",
            ref timestamp);

        return Advance(
            transactionId,
            planId,
            UpdateTransactionState.Downloading,
            UpdateTransactionState.Failed,
            journal,
            message,
            ref timestamp).State;
    }

    private static UpdateTransactionState InterruptDuringInstall(
        Guid transactionId,
        Guid planId,
        List<OperationJournalEntry> journal,
        ref DateTimeOffset timestamp)
    {
        Advance(
            transactionId,
            planId,
            UpdateTransactionState.PreflightPassed,
            UpdateTransactionState.Downloading,
            journal,
            "Simulated package download started.",
            ref timestamp);

        Advance(
            transactionId,
            planId,
            UpdateTransactionState.Downloading,
            UpdateTransactionState.Downloaded,
            journal,
            "Simulated package download completed.",
            ref timestamp);

        Advance(
            transactionId,
            planId,
            UpdateTransactionState.Downloaded,
            UpdateTransactionState.VerificationRunning,
            journal,
            "Package verification started.",
            ref timestamp);

        Advance(
            transactionId,
            planId,
            UpdateTransactionState.VerificationRunning,
            UpdateTransactionState.Verified,
            journal,
            "Package verification passed for simulation.",
            ref timestamp);

        Advance(
            transactionId,
            planId,
            UpdateTransactionState.Verified,
            UpdateTransactionState.AwaitingConsent,
            journal,
            "Simulated user consent recorded.",
            ref timestamp);

        Advance(
            transactionId,
            planId,
            UpdateTransactionState.AwaitingConsent,
            UpdateTransactionState.AwaitingElevation,
            journal,
            "Simulated elevation granted without broker install.",
            ref timestamp);

        return Advance(
            transactionId,
            planId,
            UpdateTransactionState.AwaitingElevation,
            UpdateTransactionState.Installing,
            journal,
            "Simulated process crash during driver installation.",
            ref timestamp).State;
    }

    private static UpdateTransactionState InterruptAfterInstall(
        Guid transactionId,
        Guid planId,
        List<OperationJournalEntry> journal,
        ref DateTimeOffset timestamp)
    {
        var installingState = InterruptDuringInstall(transactionId, planId, journal, ref timestamp);

        if (installingState != UpdateTransactionState.Installing)
        {
            return installingState;
        }

        Advance(
            transactionId,
            planId,
            UpdateTransactionState.Installing,
            UpdateTransactionState.InstallReturned,
            journal,
            "Simulated install API returned success.",
            ref timestamp);

        Advance(
            transactionId,
            planId,
            UpdateTransactionState.InstallReturned,
            UpdateTransactionState.RestartRequired,
            journal,
            "Simulated restart requirement detected.",
            ref timestamp);

        return Advance(
            transactionId,
            planId,
            UpdateTransactionState.RestartRequired,
            UpdateTransactionState.AwaitingReboot,
            journal,
            "Simulated unexpected reboot before post-install verification.",
            ref timestamp).State;
    }

    private static InjectedFaultResult CreateInjectedFault(
        FaultInjectionScenario scenario,
        FaultScenarioDefinition definition,
        UpdateTransactionState interruptedState)
    {
        return scenario switch
        {
            FaultInjectionScenario.ProcessCrash => new InjectedFaultResult(
                scenario,
                definition.Checkpoint,
                interruptedState,
                RequiresRecovery: true,
                "Simulated process crash left the transaction mid-install.",
                "Review the transaction journal, prepare recovery, and reconcile before retrying."),
            FaultInjectionScenario.UnexpectedReboot => new InjectedFaultResult(
                scenario,
                definition.Checkpoint,
                interruptedState,
                RequiresRecovery: true,
                "Simulated unexpected reboot interrupted post-install verification.",
                "After reboot, reconcile the transaction and resume post-install checks."),
            FaultInjectionScenario.NetworkFailure => new InjectedFaultResult(
                scenario,
                definition.Checkpoint,
                interruptedState,
                RequiresRecovery: false,
                "Simulated Windows Update network failure during download.",
                "Restore network connectivity, rescan for updates, and rebuild the plan."),
            FaultInjectionScenario.CandidateDisappeared => new InjectedFaultResult(
                scenario,
                definition.Checkpoint,
                interruptedState,
                RequiresRecovery: false,
                "Simulated Windows Update candidate disappearance.",
                "Rescan Windows Update and create a fresh plan for the device."),
            _ => throw new InvalidOperationException($"Unsupported fault scenario '{scenario}'."),
        };
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
                $"Invalid fault injection transition from '{from}' to '{to}'.");
        }

        timestamp = timestamp.AddMilliseconds(1);
        journal.Add(new OperationJournalEntry(transactionId, from, to, timestamp, message));
        return new UpdateTransaction(transactionId, planId, to, timestamp);
    }
}
