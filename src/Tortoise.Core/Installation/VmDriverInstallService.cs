using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Mutation;
using Tortoise.Core.Planning;
using Tortoise.Core.Transactions;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Installation;

public sealed class VmDriverInstallService : IVmDriverInstallService
{
    private readonly IExecutionEnvironmentDetector _environmentDetector;
    private readonly IUpdatePlanService _planService;
    private readonly IUpdatePreflightService _preflightService;
    private readonly IDeviceInventoryProvider _deviceInventoryProvider;
    private readonly IUpdateTransactionStore _transactionStore;
    private readonly IBrokerPlanValidationClient? _brokerPlanValidationClient;
    private readonly IBrokerDriverInstallClient? _brokerDriverInstallClient;
    private readonly IRealPostInstallVerificationService _postInstallVerificationService;

    public VmDriverInstallService(
        IExecutionEnvironmentDetector environmentDetector,
        IUpdatePlanService planService,
        IUpdatePreflightService preflightService,
        IDeviceInventoryProvider deviceInventoryProvider,
        IUpdateTransactionStore transactionStore,
        IRealPostInstallVerificationService postInstallVerificationService,
        IBrokerPlanValidationClient? brokerPlanValidationClient = null,
        IBrokerDriverInstallClient? brokerDriverInstallClient = null)
    {
        _environmentDetector = environmentDetector;
        _planService = planService;
        _preflightService = preflightService;
        _deviceInventoryProvider = deviceInventoryProvider;
        _transactionStore = transactionStore;
        _postInstallVerificationService = postInstallVerificationService;
        _brokerPlanValidationClient = brokerPlanValidationClient;
        _brokerDriverInstallClient = brokerDriverInstallClient;
    }

    public async Task<VmDriverInstallResult> InstallAsync(
        Guid planId,
        VmDriverInstallOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var capability = MutationCapabilityResolver.Resolve(_environmentDetector.Detect());
        if (!capability.IsEnabled || capability.Environment != MutationEnvironment.DisposableVm)
        {
            throw new MutationDeniedException(
                $"VM-gated driver installation was denied: {capability.Reason}");
        }

        using var servicingLock = MutationServicingLock.TryAcquire();
        if (!servicingLock.IsAcquired)
        {
            throw new MutationDeniedException(
                "VM-gated driver installation was denied because another Tortoise servicing operation is in progress.");
        }

        var storedPlan = await _planService.GetPlanAsync(planId, cancellationToken)
            ?? throw new InvalidOperationException($"Plan '{planId}' was not found.");

        var transactionId = Guid.NewGuid();
        var journal = new List<OperationJournalEntry>();
        var timestamp = DateTimeOffset.UtcNow;
        var transaction = new UpdateTransaction(
            transactionId,
            storedPlan.PlanId,
            UpdateTransactionState.Discovered,
            timestamp);

        transaction = Advance(transaction, UpdateTransactionState.Eligible, journal, "VM install transaction opened.", ref timestamp);
        transaction = Advance(transaction, UpdateTransactionState.Planned, journal, "Frozen plan loaded for VM install.", ref timestamp);
        transaction = Advance(transaction, UpdateTransactionState.PreflightRunning, journal, "Preflight started.", ref timestamp);

        if (storedPlan.RiskLevel != DriverRiskLevel.Low
            || storedPlan.Classification != UpdateClassification.WindowsRecommended)
        {
            return await FailAsync(
                storedPlan.PlanId,
                transaction,
                journal,
                UpdateTransactionState.PreflightFailed,
                null,
                null,
                null,
                null,
                $"Plan '{storedPlan.PlanId}' is not eligible for disposable VM install. Only low-risk Windows-recommended updates are allowed.",
                cancellationToken);
        }

        var inventoryBefore = await _deviceInventoryProvider.ScanAsync(cancellationToken);
        var currentDevice = inventoryBefore.Devices.SingleOrDefault(device =>
                string.Equals(
                    device.Snapshot.Identity.DeviceInstanceId,
                    storedPlan.Plan.DeviceSnapshot.Identity.DeviceInstanceId,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Target device is no longer present in the current inventory.");

        var preflight = _preflightService.RunPreflight(storedPlan, currentDevice, capability);
        if (preflight.IsBlocked)
        {
            return await FailAsync(
                storedPlan.PlanId,
                transaction,
                journal,
                UpdateTransactionState.PreflightFailed,
                preflight,
                null,
                null,
                null,
                "Preflight blocked the disposable VM install.",
                cancellationToken);
        }

        transaction = Advance(transaction, UpdateTransactionState.PreflightPassed, journal, "Preflight passed.", ref timestamp);
        await UpdateTransactionJournal.PersistAsync(_transactionStore, transaction, journal, cancellationToken);

        transaction = Advance(
            transaction,
            UpdateTransactionState.AwaitingConsent,
            journal,
            "VM install consent implied by lab invocation.",
            ref timestamp);
        transaction = Advance(
            transaction,
            UpdateTransactionState.AwaitingElevation,
            journal,
            "Broker elevation requested.",
            ref timestamp);
        await UpdateTransactionJournal.PersistAsync(_transactionStore, transaction, journal, cancellationToken);

        BrokerPlanValidationResult? brokerValidation = null;
        if (options.RequireBrokerValidation)
        {
            if (_brokerPlanValidationClient is null || options.BrokerOptions is null)
            {
                throw new InvalidOperationException(
                    "Broker plan validation was requested but broker options or client are unavailable.");
            }

            brokerValidation = await _brokerPlanValidationClient.ValidatePlanAsync(
                storedPlan.PlanId,
                storedPlan.Plan.PlanHash,
                options.BrokerOptions,
                cancellationToken);

            if (!brokerValidation.Succeeded)
            {
                return await FailAsync(
                    storedPlan.PlanId,
                    transaction,
                    journal,
                    UpdateTransactionState.Failed,
                    preflight,
                    brokerValidation,
                    null,
                    null,
                    $"Broker plan validation failed: {brokerValidation.Message}",
                    cancellationToken);
            }
        }

        if (_brokerDriverInstallClient is null || options.BrokerOptions is null)
        {
            throw new InvalidOperationException(
                "Broker driver install client or broker options are unavailable.");
        }

        transaction = Advance(
            transaction,
            UpdateTransactionState.Installing,
            journal,
            "Elevated WUA download+install boundary entered.",
            ref timestamp);
        await UpdateTransactionJournal.PersistAsync(_transactionStore, transaction, journal, cancellationToken);

        var candidate = storedPlan.Plan.ProposedUpdate.Candidate;
        var brokerInstall = await _brokerDriverInstallClient.InstallDriverAsync(
            storedPlan.PlanId,
            storedPlan.Plan.PlanHash,
            candidate.UpdateIdentity,
            candidate.UpdateRevision,
            options.BrokerOptions,
            cancellationToken);

        if (!brokerInstall.Succeeded)
        {
            return await FailAsync(
                storedPlan.PlanId,
                transaction,
                journal,
                UpdateTransactionState.Failed,
                preflight,
                brokerValidation,
                brokerInstall,
                null,
                $"Broker driver install failed: {brokerInstall.Message}",
                cancellationToken);
        }

        transaction = Advance(
            transaction,
            UpdateTransactionState.InstallReturned,
            journal,
            "Broker driver install returned.",
            ref timestamp);
        await UpdateTransactionJournal.PersistAsync(_transactionStore, transaction, journal, cancellationToken);

        if (brokerInstall.RebootRequired)
        {
            transaction = Advance(
                transaction,
                UpdateTransactionState.RestartRequired,
                journal,
                "Windows Update reported that a reboot is required before verification can complete.",
                ref timestamp);
            transaction = Advance(
                transaction,
                UpdateTransactionState.AwaitingReboot,
                journal,
                "VM install is awaiting reboot; transaction remains open.",
                ref timestamp);
            await UpdateTransactionJournal.PersistAsync(_transactionStore, transaction, journal, cancellationToken);

            return new VmDriverInstallResult(
                storedPlan.PlanId,
                preflight,
                brokerValidation,
                brokerInstall,
                new UpdateVerification(
                    transactionId,
                    UpdateVerificationResult.InstalledRestartRequired,
                    "Driver install returned but reboot is required before Tortoise can verify completion.",
                    DateTimeOffset.UtcNow),
                CompletedSuccessfully: false,
                Summary: "Disposable VM install requires a reboot before it can be marked complete.");
        }

        transaction = Advance(
            transaction,
            UpdateTransactionState.PostInstallChecking,
            journal,
            "Post-install verification started.",
            ref timestamp);

        var inventoryAfter = await _deviceInventoryProvider.ScanAsync(cancellationToken);
        var deviceAfter = inventoryAfter.Devices.SingleOrDefault(device =>
                string.Equals(
                    device.Snapshot.Identity.DeviceInstanceId,
                    storedPlan.Plan.DeviceSnapshot.Identity.DeviceInstanceId,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Target device disappeared after install.");

        var postInstallVerification = _postInstallVerificationService.VerifyPostInstall(
            storedPlan,
            currentDevice,
            deviceAfter,
            transactionId);

        if (postInstallVerification.Result == UpdateVerificationResult.Failed)
        {
            return await FailAsync(
                storedPlan.PlanId,
                transaction,
                journal,
                UpdateTransactionState.Failed,
                preflight,
                brokerValidation,
                brokerInstall,
                postInstallVerification,
                postInstallVerification.Message,
                cancellationToken);
        }

        transaction = Advance(
            transaction,
            UpdateTransactionState.Completed,
            journal,
            "VM install completed and post-install verification passed.",
            ref timestamp);
        await UpdateTransactionJournal.PersistAsync(_transactionStore, transaction, journal, cancellationToken);

        return new VmDriverInstallResult(
            storedPlan.PlanId,
            preflight,
            brokerValidation,
            brokerInstall,
            postInstallVerification,
            CompletedSuccessfully: true,
            Summary: "Disposable VM install completed and post-install verification passed.");
    }

    private async Task<VmDriverInstallResult> FailAsync(
        Guid planId,
        UpdateTransaction transaction,
        List<OperationJournalEntry> journal,
        UpdateTransactionState terminalState,
        UpdatePreflight? preflight,
        BrokerPlanValidationResult? brokerValidation,
        BrokerDriverInstallResult? brokerInstall,
        UpdateVerification? postInstallVerification,
        string summary,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow;
        transaction = Advance(transaction, terminalState, journal, summary, ref timestamp);
        await UpdateTransactionJournal.PersistAsync(_transactionStore, transaction, journal, cancellationToken);

        return new VmDriverInstallResult(
            planId,
            preflight ?? new UpdatePreflight(planId, [], true),
            brokerValidation,
            brokerInstall,
            postInstallVerification
                ?? new UpdateVerification(
                    transaction.TransactionId,
                    UpdateVerificationResult.Failed,
                    summary,
                    DateTimeOffset.UtcNow),
            CompletedSuccessfully: false,
            Summary: summary);
    }

    private static UpdateTransaction Advance(
        UpdateTransaction transaction,
        UpdateTransactionState to,
        IList<OperationJournalEntry> journal,
        string message,
        ref DateTimeOffset timestamp) =>
        UpdateTransactionJournal.Advance(transaction, to, journal, message, ref timestamp);
}
