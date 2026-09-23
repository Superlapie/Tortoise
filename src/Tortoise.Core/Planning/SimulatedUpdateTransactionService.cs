using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Transactions;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Planning;

public sealed record SimulatedWorkflowExecutionOptions(
    bool RequireBrokerValidation = false,
    BrokerPlanValidationOptions? BrokerOptions = null);

public sealed record SimulatedTransactionResult(
    UpdateTransactionRecord Transaction,
    UpdatePreflight Preflight,
    bool ReachedAwaitingConsent,
    string Summary);

public sealed record SimulatedFullWorkflowResult(
    UpdateTransactionRecord Transaction,
    UpdatePreflight Preflight,
    BrokerPlanValidationResult? BrokerValidation,
    UpdateVerification PackageVerification,
    UpdateVerification PostInstallVerification,
    bool CompletedSuccessfully,
    string Summary);

public interface ISimulatedUpdateTransactionService
{
    Task<SimulatedTransactionResult> SimulateAsync(
        Guid planId,
        IMutationCapability mutationCapability,
        CancellationToken cancellationToken = default);

    Task<SimulatedFullWorkflowResult> SimulateFullWorkflowAsync(
        Guid planId,
        IMutationCapability mutationCapability,
        SimulatedWorkflowExecutionOptions? executionOptions = null,
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

    Task<IReadOnlyList<UpdateTransactionRecord>> ListAllAsync(
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
    private readonly ISimulatedUpdatePackageVerificationService _packageVerificationService;
    private readonly ISimulatedPostInstallVerificationService _postInstallVerificationService;
    private readonly IBrokerPlanValidationClient? _brokerPlanValidationClient;

    public SimulatedUpdateTransactionService(
        IUpdatePlanService planService,
        IUpdatePreflightService preflightService,
        IUpdateTransactionStore transactionStore,
        IDeviceInventoryProvider deviceInventoryProvider,
        ISimulatedUpdatePackageVerificationService packageVerificationService,
        ISimulatedPostInstallVerificationService postInstallVerificationService,
        IBrokerPlanValidationClient? brokerPlanValidationClient = null)
    {
        _planService = planService;
        _preflightService = preflightService;
        _transactionStore = transactionStore;
        _deviceInventoryProvider = deviceInventoryProvider;
        _packageVerificationService = packageVerificationService;
        _postInstallVerificationService = postInstallVerificationService;
        _brokerPlanValidationClient = brokerPlanValidationClient;
    }

    public async Task<SimulatedTransactionResult> SimulateAsync(
        Guid planId,
        IMutationCapability mutationCapability,
        CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(planId, mutationCapability, cancellationToken);
        if (context.Preflight.IsBlocked)
        {
            return await FailAtPreflightAsync(context, cancellationToken);
        }

        var timestamp = context.Timestamp;
        var transaction = Advance(
            context.TransactionId,
            context.StoredPlan.PlanId,
            UpdateTransactionState.PreflightPassed,
            UpdateTransactionState.AwaitingConsent,
            context.Journal,
            "Simulation halted before elevation. Mutation remains disabled.",
            ref timestamp);

        var completedRecord = new UpdateTransactionRecord(transaction, context.Journal);
        await _transactionStore.SaveAsync(completedRecord, cancellationToken);

        return new SimulatedTransactionResult(
            completedRecord,
            context.Preflight,
            ReachedAwaitingConsent: true,
            Summary: "Simulation reached awaiting-consent without performing any driver mutation.");
    }

    public async Task<SimulatedFullWorkflowResult> SimulateFullWorkflowAsync(
        Guid planId,
        IMutationCapability mutationCapability,
        SimulatedWorkflowExecutionOptions? executionOptions = null,
        CancellationToken cancellationToken = default)
    {
        executionOptions ??= new SimulatedWorkflowExecutionOptions();
        var context = await PrepareAsync(planId, mutationCapability, cancellationToken);
        if (context.Preflight.IsBlocked)
        {
            var failedRecord = await FailAtPreflightAsync(context, cancellationToken);
            return new SimulatedFullWorkflowResult(
                failedRecord.Transaction,
                failedRecord.Preflight,
                BrokerValidation: null,
                PackageVerification: CreateSkippedVerification(context.TransactionId, "Package verification skipped after preflight failure."),
                PostInstallVerification: CreateSkippedVerification(context.TransactionId, "Post-install verification skipped after preflight failure."),
                CompletedSuccessfully: false,
                Summary: failedRecord.Summary);
        }

        var timestamp = context.Timestamp;
        BrokerPlanValidationResult? brokerValidation = null;
        if (executionOptions.RequireBrokerValidation)
        {
            brokerValidation = await ValidateBrokerPlanAsync(context, executionOptions, cancellationToken);
            if (!brokerValidation.Succeeded)
            {
                var transaction = Advance(
                    context.TransactionId,
                    context.StoredPlan.PlanId,
                    UpdateTransactionState.PreflightPassed,
                    UpdateTransactionState.Failed,
                    context.Journal,
                    brokerValidation.Message,
                    ref timestamp);

                var failedRecord = new UpdateTransactionRecord(transaction, context.Journal);
                await _transactionStore.SaveAsync(failedRecord, cancellationToken);

                return new SimulatedFullWorkflowResult(
                    failedRecord,
                    context.Preflight,
                    brokerValidation,
                    CreateSkippedVerification(context.TransactionId, "Package verification skipped after broker validation failure."),
                    CreateSkippedVerification(context.TransactionId, "Post-install verification skipped after broker validation failure."),
                    CompletedSuccessfully: false,
                    Summary: $"Simulated workflow stopped after broker plan validation failed: {brokerValidation.Message}");
            }
        }

        var workflowTransaction = Advance(
            context.TransactionId,
            context.StoredPlan.PlanId,
            UpdateTransactionState.PreflightPassed,
            UpdateTransactionState.Downloading,
            context.Journal,
            "Simulated package download started.",
            ref timestamp);

        workflowTransaction = Advance(
            workflowTransaction.TransactionId,
            context.StoredPlan.PlanId,
            workflowTransaction.State,
            UpdateTransactionState.Downloaded,
            context.Journal,
            "Simulated package download completed.",
            ref timestamp);

        workflowTransaction = Advance(
            workflowTransaction.TransactionId,
            context.StoredPlan.PlanId,
            workflowTransaction.State,
            UpdateTransactionState.VerificationRunning,
            context.Journal,
            "Package verification started.",
            ref timestamp);

        var packageVerification = _packageVerificationService.VerifyPackage(
            context.StoredPlan,
            context.TransactionId);

        if (packageVerification.Result == UpdateVerificationResult.Failed)
        {
            workflowTransaction = Advance(
                workflowTransaction.TransactionId,
                context.StoredPlan.PlanId,
                workflowTransaction.State,
                UpdateTransactionState.Failed,
                context.Journal,
                packageVerification.Message,
                ref timestamp);

            var failedRecord = new UpdateTransactionRecord(workflowTransaction, context.Journal);
            await _transactionStore.SaveAsync(failedRecord, cancellationToken);

            return new SimulatedFullWorkflowResult(
                failedRecord,
                context.Preflight,
                brokerValidation,
                packageVerification,
                CreateSkippedVerification(context.TransactionId, "Post-install verification skipped after package verification failure."),
                CompletedSuccessfully: false,
                Summary: "Simulated workflow stopped at package verification failure. No mutation was attempted.");
        }

        workflowTransaction = Advance(
            workflowTransaction.TransactionId,
            context.StoredPlan.PlanId,
            workflowTransaction.State,
            UpdateTransactionState.Verified,
            context.Journal,
            "Package verification passed for simulation.",
            ref timestamp);

        workflowTransaction = Advance(
            workflowTransaction.TransactionId,
            context.StoredPlan.PlanId,
            workflowTransaction.State,
            UpdateTransactionState.AwaitingConsent,
            context.Journal,
            "Simulated user consent recorded.",
            ref timestamp);

        workflowTransaction = Advance(
            workflowTransaction.TransactionId,
            context.StoredPlan.PlanId,
            workflowTransaction.State,
            UpdateTransactionState.AwaitingElevation,
            context.Journal,
            "Simulated elevation granted without broker install.",
            ref timestamp);

        workflowTransaction = Advance(
            workflowTransaction.TransactionId,
            context.StoredPlan.PlanId,
            workflowTransaction.State,
            UpdateTransactionState.Installing,
            context.Journal,
            "Simulated driver installation started (no system mutation).",
            ref timestamp);

        workflowTransaction = Advance(
            workflowTransaction.TransactionId,
            context.StoredPlan.PlanId,
            workflowTransaction.State,
            UpdateTransactionState.InstallReturned,
            context.Journal,
            "Simulated install API returned success.",
            ref timestamp);

        workflowTransaction = Advance(
            workflowTransaction.TransactionId,
            context.StoredPlan.PlanId,
            workflowTransaction.State,
            UpdateTransactionState.PostInstallChecking,
            context.Journal,
            "Post-install verification started.",
            ref timestamp);

        var inventoryAfter = await _deviceInventoryProvider.ScanAsync(cancellationToken);
        var deviceAfter = inventoryAfter.Devices.Single(device =>
            string.Equals(
                device.Snapshot.Identity.DeviceInstanceId,
                context.CurrentDevice.Snapshot.Identity.DeviceInstanceId,
                StringComparison.OrdinalIgnoreCase));

        var postInstallVerification = _postInstallVerificationService.VerifyPostInstall(
            context.StoredPlan,
            context.CurrentDevice,
            deviceAfter,
            context.TransactionId);

        if (postInstallVerification.Result == UpdateVerificationResult.Failed)
        {
            workflowTransaction = Advance(
                workflowTransaction.TransactionId,
                context.StoredPlan.PlanId,
                workflowTransaction.State,
                UpdateTransactionState.Failed,
                context.Journal,
                postInstallVerification.Message,
                ref timestamp);

            var failedRecord = new UpdateTransactionRecord(workflowTransaction, context.Journal);
            await _transactionStore.SaveAsync(failedRecord, cancellationToken);

            return new SimulatedFullWorkflowResult(
                failedRecord,
                context.Preflight,
                brokerValidation,
                packageVerification,
                postInstallVerification,
                CompletedSuccessfully: false,
                Summary: "Simulated workflow stopped at post-install verification failure. No mutation was attempted.");
        }

        workflowTransaction = Advance(
            workflowTransaction.TransactionId,
            context.StoredPlan.PlanId,
            workflowTransaction.State,
            UpdateTransactionState.Completed,
            context.Journal,
            "Simulated workflow completed successfully.",
            ref timestamp);

        var completedRecord = new UpdateTransactionRecord(workflowTransaction, context.Journal);
        await _transactionStore.SaveAsync(completedRecord, cancellationToken);

        var summary = brokerValidation is null
            ? "Simulated workflow completed end-to-end without performing any driver mutation."
            : "Simulated workflow completed end-to-end without performing any driver mutation. Broker plan validation succeeded.";

        return new SimulatedFullWorkflowResult(
            completedRecord,
            context.Preflight,
            brokerValidation,
            packageVerification,
            postInstallVerification,
            CompletedSuccessfully: true,
            summary);
    }

    private async Task<BrokerPlanValidationResult> ValidateBrokerPlanAsync(
        SimulationContext context,
        SimulatedWorkflowExecutionOptions executionOptions,
        CancellationToken cancellationToken)
    {
        if (_brokerPlanValidationClient is null)
        {
            throw new InvalidOperationException(
                "Broker plan validation was requested but no broker validation client is registered.");
        }

        if (executionOptions.BrokerOptions is null)
        {
            throw new InvalidOperationException(
                "Broker plan validation requires broker session options.");
        }

        return await _brokerPlanValidationClient.ValidatePlanAsync(
            context.StoredPlan.PlanId,
            context.StoredPlan.Plan.PlanHash,
            executionOptions.BrokerOptions,
            cancellationToken);
    }

    private async Task<SimulationContext> PrepareAsync(
        Guid planId,
        IMutationCapability mutationCapability,
        CancellationToken cancellationToken)
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

        Advance(
            transactionId,
            storedPlan.PlanId,
            UpdateTransactionState.Discovered,
            UpdateTransactionState.Eligible,
            journal,
            "Recommendation converted to an eligible update transaction.",
            ref now);

        Advance(
            transactionId,
            storedPlan.PlanId,
            UpdateTransactionState.Eligible,
            UpdateTransactionState.Planned,
            journal,
            "Frozen plan attached to transaction.",
            ref now);

        Advance(
            transactionId,
            storedPlan.PlanId,
            UpdateTransactionState.Planned,
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
            Advance(
                transactionId,
                storedPlan.PlanId,
                UpdateTransactionState.PreflightRunning,
                UpdateTransactionState.PreflightFailed,
                journal,
                "Preflight blocked the simulated workflow.",
                ref now);
        }
        else
        {
            Advance(
                transactionId,
                storedPlan.PlanId,
                UpdateTransactionState.PreflightRunning,
                UpdateTransactionState.PreflightPassed,
                journal,
                "Preflight checks passed for simulation.",
                ref now);
        }

        return new SimulationContext(
            storedPlan,
            currentDevice,
            preflight,
            transactionId,
            journal,
            now);
    }

    private async Task<SimulatedTransactionResult> FailAtPreflightAsync(
        SimulationContext context,
        CancellationToken cancellationToken)
    {
        var failedRecord = new UpdateTransactionRecord(
            new UpdateTransaction(
                context.TransactionId,
                context.StoredPlan.PlanId,
                UpdateTransactionState.PreflightFailed,
                context.Timestamp),
            context.Journal);

        await _transactionStore.SaveAsync(failedRecord, cancellationToken);

        return new SimulatedTransactionResult(
            failedRecord,
            context.Preflight,
            ReachedAwaitingConsent: false,
            Summary: "Simulation stopped at preflight failure. No mutation was attempted.");
    }

    private static UpdateVerification CreateSkippedVerification(Guid transactionId, string message) =>
        new(transactionId, UpdateVerificationResult.Failed, message, DateTimeOffset.UtcNow);

    private static UpdateTransaction Advance(
        Guid transactionId,
        Guid planId,
        UpdateTransactionState from,
        UpdateTransactionState to,
        IList<OperationJournalEntry> journal,
        string message,
        ref DateTimeOffset timestamp)
    {
        var transaction = new UpdateTransaction(transactionId, planId, from, timestamp);
        return UpdateTransactionJournal.Advance(transaction, to, journal, message, ref timestamp);
    }

    private sealed record SimulationContext(
        StoredUpdatePlan StoredPlan,
        DeviceInventoryEntry CurrentDevice,
        UpdatePreflight Preflight,
        Guid TransactionId,
        List<OperationJournalEntry> Journal,
        DateTimeOffset Timestamp);
}
