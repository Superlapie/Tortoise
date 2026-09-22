using Tortoise.Core.Drivers;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Recovery;

public sealed record RecoveryExportAttempt(
    bool ExportServiceEnabled,
    bool Succeeded,
    string? ExportedPackagePath,
    string Message,
    string? MatchedPackageName);

public sealed record RecoveryPreparationRecord(
    Guid PreparationId,
    Guid PlanId,
    Guid? TransactionId,
    DateTimeOffset PreparedAtUtc,
    RecoverySnapshot Snapshot,
    SystemSnapshot SystemSnapshot,
    RecoveryExportAttempt ExportAttempt,
    SystemRestoreInfo SystemRestore,
    UpdatePlan Plan);

public sealed record RecoveryPreparationSummary(
    Guid PreparationId,
    Guid PlanId,
    DateTimeOffset PreparedAtUtc,
    string DeviceName,
    bool ExportSucceeded,
    bool SystemRestoreEnabled);

public interface IRecoveryPreparationService
{
    Task<RecoveryPreparationRecord> PrepareAsync(
        Guid planId,
        string? exportDirectory = null,
        CancellationToken cancellationToken = default);

    Task<RecoveryPreparationRecord?> GetAsync(
        Guid preparationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecoveryPreparationSummary>> ListAsync(
        Guid? planId = null,
        CancellationToken cancellationToken = default);
}

public interface IRecoveryPreparationStore
{
    Task SaveAsync(RecoveryPreparationRecord record, CancellationToken cancellationToken = default);

    Task<RecoveryPreparationRecord?> GetAsync(
        Guid preparationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecoveryPreparationSummary>> ListAsync(
        Guid? planId = null,
        CancellationToken cancellationToken = default);
}
