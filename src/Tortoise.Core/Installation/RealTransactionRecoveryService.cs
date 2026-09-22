using Tortoise.Core.Devices;
using Tortoise.Core.Planning;
using Tortoise.Core.Transactions;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Installation;

public sealed record RealTransactionRecoveryResult(
    Guid TransactionId,
    UpdateTransactionState FinalState,
    bool CompletedSuccessfully,
    string Summary,
    UpdateVerification? PostInstallVerification = null);

public interface IRealTransactionRecoveryService
{
    Task<RealTransactionRecoveryResult> ResumeAfterRebootAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);

    Task<RealTransactionRecoveryResult> ReconcileIncompleteAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);
}

public sealed class RealTransactionRecoveryService : IRealTransactionRecoveryService
{
    private readonly IUpdateTransactionStore _transactionStore;
    private readonly IUpdatePlanService _planService;
    private readonly IDeviceInventoryProvider _deviceInventoryProvider;
    private readonly IRealPostInstallVerificationService _postInstallVerificationService;

    public RealTransactionRecoveryService(
        IUpdateTransactionStore transactionStore,
        IUpdatePlanService planService,
        IDeviceInventoryProvider deviceInventoryProvider,
        IRealPostInstallVerificationService postInstallVerificationService)
    {
        _transactionStore = transactionStore;
        _planService = planService;
        _deviceInventoryProvider = deviceInventoryProvider;
        _postInstallVerificationService = postInstallVerificationService;
    }

    public async Task<RealTransactionRecoveryResult> ResumeAfterRebootAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        var record = await RequireRecordAsync(transactionId, cancellationToken);
        if (record.Transaction.State != UpdateTransactionState.AwaitingReboot)
        {
            throw new InvalidOperationException(
                $"Transaction '{transactionId}' is not awaiting reboot (state={record.Transaction.State}).");
        }

        return await CompletePostRebootVerificationAsync(record, cancellationToken);
    }

    public async Task<RealTransactionRecoveryResult> ReconcileIncompleteAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        var record = await RequireRecordAsync(transactionId, cancellationToken);
        return record.Transaction.State switch
        {
            UpdateTransactionState.AwaitingReboot => await CompletePostRebootVerificationAsync(record, cancellationToken),
            UpdateTransactionState.RestartRequired => await ReconcileRestartRequiredAsync(record, cancellationToken),
            UpdateTransactionState.Installing or UpdateTransactionState.InstallReturned =>
                await ReconcileAmbiguousInstallAsync(record, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Transaction '{transactionId}' is not in a recoverable incomplete state (state={record.Transaction.State})."),
        };
    }

    private async Task<RealTransactionRecoveryResult> CompletePostRebootVerificationAsync(
        UpdateTransactionRecord record,
        CancellationToken cancellationToken)
    {
        var storedPlan = await _planService.GetPlanAsync(record.Transaction.PlanId, cancellationToken)
            ?? throw new InvalidOperationException($"Plan '{record.Transaction.PlanId}' was not found.");

        var inventory = await _deviceInventoryProvider.ScanAsync(cancellationToken);
        var deviceAfter = inventory.Devices.SingleOrDefault(device =>
                string.Equals(
                    device.Snapshot.Identity.DeviceInstanceId,
                    storedPlan.Plan.DeviceSnapshot.Identity.DeviceInstanceId,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Target device disappeared during reboot recovery.");

        var journal = record.Journal.ToList();
        var transaction = record.Transaction;
        var timestamp = DateTimeOffset.UtcNow;
        transaction = Advance(
            transaction,
            UpdateTransactionState.PostRebootChecking,
            journal,
            "Post-reboot verification started.",
            ref timestamp);

        var baselineDevice = CreateBaselineDevice(storedPlan);
        var verification = _postInstallVerificationService.VerifyPostInstall(
            storedPlan,
            baselineDevice,
            deviceAfter,
            transaction.TransactionId);

        if (verification.Result == UpdateVerificationResult.Failed)
        {
            transaction = Advance(transaction, UpdateTransactionState.Failed, journal, verification.Message, ref timestamp);
            await _transactionStore.SaveAsync(new UpdateTransactionRecord(transaction, journal), cancellationToken);
            return new RealTransactionRecoveryResult(
                transaction.TransactionId,
                transaction.State,
                false,
                verification.Message,
                verification);
        }

        transaction = Advance(
            transaction,
            UpdateTransactionState.Completed,
            journal,
            "Post-reboot verification passed.",
            ref timestamp);
        await _transactionStore.SaveAsync(new UpdateTransactionRecord(transaction, journal), cancellationToken);

        return new RealTransactionRecoveryResult(
            transaction.TransactionId,
            transaction.State,
            true,
            "Transaction completed after reboot.",
            verification);
    }

    private async Task<RealTransactionRecoveryResult> ReconcileRestartRequiredAsync(
        UpdateTransactionRecord record,
        CancellationToken cancellationToken)
    {
        var journal = record.Journal.ToList();
        var transaction = record.Transaction;
        var timestamp = DateTimeOffset.UtcNow;
        transaction = Advance(
            transaction,
            UpdateTransactionState.AwaitingReboot,
            journal,
            "Restart requirement reconciled after interruption; transaction remains open until reboot verification.",
            ref timestamp);

        await _transactionStore.SaveAsync(new UpdateTransactionRecord(transaction, journal), cancellationToken);

        return new RealTransactionRecoveryResult(
            transaction.TransactionId,
            transaction.State,
            false,
            "Transaction reconciled to awaiting reboot.",
            new UpdateVerification(
                transaction.TransactionId,
                UpdateVerificationResult.InstalledRestartRequired,
                "Install reported restart required before Tortoise persisted awaiting reboot.",
                DateTimeOffset.UtcNow));
    }

    private async Task<RealTransactionRecoveryResult> ReconcileAmbiguousInstallAsync(
        UpdateTransactionRecord record,
        CancellationToken cancellationToken)
    {
        var storedPlan = await _planService.GetPlanAsync(record.Transaction.PlanId, cancellationToken)
            ?? throw new InvalidOperationException($"Plan '{record.Transaction.PlanId}' was not found.");

        var inventory = await _deviceInventoryProvider.ScanAsync(cancellationToken);
        var deviceAfter = inventory.Devices.SingleOrDefault(device =>
                string.Equals(
                    device.Snapshot.Identity.DeviceInstanceId,
                    storedPlan.Plan.DeviceSnapshot.Identity.DeviceInstanceId,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Target device disappeared during install reconciliation.");

        var journal = record.Journal.ToList();
        var transaction = record.Transaction;
        var timestamp = DateTimeOffset.UtcNow;
        transaction = Advance(
            transaction,
            UpdateTransactionState.Failed,
            journal,
            "Install outcome was ambiguous after interruption.",
            ref timestamp);
        transaction = Advance(
            transaction,
            UpdateTransactionState.RecoveryRequired,
            journal,
            "Manual inspection is required before retrying.",
            ref timestamp);

        var baselineDevice = CreateBaselineDevice(storedPlan);
        var verification = _postInstallVerificationService.VerifyPostInstall(
            storedPlan,
            baselineDevice,
            deviceAfter,
            transaction.TransactionId);

        await _transactionStore.SaveAsync(new UpdateTransactionRecord(transaction, journal), cancellationToken);

        var completed = verification.Result == UpdateVerificationResult.Verified;
        var summary = completed
            ? "Interrupted install appears to have succeeded; transaction moved to recovery-required for manual confirmation."
            : "Interrupted install outcome is inconclusive; do not retry automatically.";

        return new RealTransactionRecoveryResult(
            transaction.TransactionId,
            transaction.State,
            completed,
            summary,
            verification);
    }

    private async Task<UpdateTransactionRecord> RequireRecordAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        return await _transactionStore.GetAsync(transactionId, cancellationToken)
            ?? throw new InvalidOperationException($"Transaction '{transactionId}' was not found.");
    }

    private static DeviceInventoryEntry CreateBaselineDevice(StoredUpdatePlan storedPlan)
    {
        var current = storedPlan.Plan.CurrentDriver;
        return new DeviceInventoryEntry(
            storedPlan.Plan.DeviceSnapshot,
            new DeviceDriverBinding(
                current.Package.Identity.ProviderName,
                current.Package.Identity.DriverVersion,
                current.Package.Identity.DriverDate,
                current.Package.Identity.PublishedInfName));
    }

    private static UpdateTransaction Advance(
        UpdateTransaction transaction,
        UpdateTransactionState to,
        IList<OperationJournalEntry> journal,
        string message,
        ref DateTimeOffset timestamp) =>
        UpdateTransactionJournal.Advance(transaction, to, journal, message, ref timestamp);
}
