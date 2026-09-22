namespace Tortoise.Core.Transactions;

public enum UpdateTransactionState
{
    Discovered = 0,
    Eligible = 1,
    Planned = 2,
    PreflightRunning = 3,
    PreflightPassed = 4,
    PreflightFailed = 5,
    Downloading = 6,
    Downloaded = 7,
    VerificationRunning = 8,
    Verified = 9,
    AwaitingConsent = 10,
    AwaitingElevation = 11,
    Installing = 12,
    InstallReturned = 13,
    RestartRequired = 14,
    PostInstallChecking = 15,
    AwaitingReboot = 16,
    PostRebootChecking = 17,
    Completed = 18,
    Failed = 19,
    RecoveryRequired = 20,
    Cancelled = 21,
}

public sealed record UpdateTransaction(
    Guid TransactionId,
    Guid PlanId,
    UpdateTransactionState State,
    DateTimeOffset UpdatedAtUtc);

public sealed record OperationJournalEntry(
    Guid TransactionId,
    UpdateTransactionState FromState,
    UpdateTransactionState ToState,
    DateTimeOffset TimestampUtc,
    string? Message);

public static class UpdateTransactionTransitions
{
    private static readonly IReadOnlyDictionary<UpdateTransactionState, UpdateTransactionState[]> Allowed =
        new Dictionary<UpdateTransactionState, UpdateTransactionState[]>
        {
            [UpdateTransactionState.Discovered] = [UpdateTransactionState.Eligible, UpdateTransactionState.Cancelled],
            [UpdateTransactionState.Eligible] = [UpdateTransactionState.Planned, UpdateTransactionState.Cancelled],
            [UpdateTransactionState.Planned] = [UpdateTransactionState.PreflightRunning, UpdateTransactionState.Cancelled],
            [UpdateTransactionState.PreflightRunning] =
                [UpdateTransactionState.PreflightPassed, UpdateTransactionState.PreflightFailed],
            [UpdateTransactionState.PreflightPassed] =
                [UpdateTransactionState.Downloading, UpdateTransactionState.AwaitingConsent, UpdateTransactionState.Failed],
            [UpdateTransactionState.PreflightFailed] = [UpdateTransactionState.Failed, UpdateTransactionState.Cancelled],
            [UpdateTransactionState.Downloading] =
                [UpdateTransactionState.Downloaded, UpdateTransactionState.Failed, UpdateTransactionState.Cancelled],
            [UpdateTransactionState.Downloaded] = [UpdateTransactionState.VerificationRunning],
            [UpdateTransactionState.VerificationRunning] =
                [UpdateTransactionState.Verified, UpdateTransactionState.Failed],
            [UpdateTransactionState.Verified] =
                [UpdateTransactionState.AwaitingConsent, UpdateTransactionState.AwaitingElevation],
            [UpdateTransactionState.AwaitingConsent] =
                [UpdateTransactionState.AwaitingElevation, UpdateTransactionState.Cancelled],
            [UpdateTransactionState.AwaitingElevation] =
                [UpdateTransactionState.Installing, UpdateTransactionState.Cancelled, UpdateTransactionState.Failed],
            [UpdateTransactionState.Installing] =
                [UpdateTransactionState.InstallReturned, UpdateTransactionState.Failed],
            [UpdateTransactionState.InstallReturned] =
                [UpdateTransactionState.RestartRequired, UpdateTransactionState.PostInstallChecking, UpdateTransactionState.Failed],
            [UpdateTransactionState.RestartRequired] = [UpdateTransactionState.AwaitingReboot],
            [UpdateTransactionState.PostInstallChecking] =
                [UpdateTransactionState.Completed, UpdateTransactionState.Failed, UpdateTransactionState.RecoveryRequired],
            [UpdateTransactionState.AwaitingReboot] = [UpdateTransactionState.PostRebootChecking],
            [UpdateTransactionState.PostRebootChecking] =
                [UpdateTransactionState.Completed, UpdateTransactionState.Failed, UpdateTransactionState.RecoveryRequired],
            [UpdateTransactionState.Completed] = [],
            [UpdateTransactionState.Failed] = [UpdateTransactionState.RecoveryRequired],
            [UpdateTransactionState.RecoveryRequired] = [],
            [UpdateTransactionState.Cancelled] = [],
        };

    public static bool CanTransition(UpdateTransactionState from, UpdateTransactionState to)
    {
        return Allowed.TryGetValue(from, out var targets) && targets.Contains(to);
    }
}
