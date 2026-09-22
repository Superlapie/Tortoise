namespace Tortoise.Core.Pilot;

public enum PilotChecklistItemStatus
{
    Passed = 0,
    Warning = 1,
    Blocked = 2,
}

public sealed record PilotChecklistItem(
    string Id,
    string Title,
    string Detail,
    PilotChecklistItemStatus Status);

public sealed record PilotChecklistResult(
    Guid PlanId,
    IReadOnlyList<PilotChecklistItem> Items,
    bool IsReady,
    string Summary);

public sealed record PhysicalPilotConfirmationRequest(
    Guid PlanId,
    string ConfirmationPhrase,
    IReadOnlyList<string> AcknowledgedItemIds);

public sealed record PhysicalPilotConfirmationRecord(
    Guid ConfirmationId,
    Guid PlanId,
    DateTimeOffset ConfirmedAtUtc,
    string ConfirmationPhrase,
    IReadOnlyList<string> AcknowledgedItemIds,
    bool ChecklistReady,
    string Summary);

public sealed record PhysicalPilotConfirmationResult(
    PhysicalPilotConfirmationRecord Record,
    bool Accepted,
    string Summary);

public interface IPhysicalPilotChecklistService
{
    Task<PilotChecklistResult> EvaluateAsync(
        Guid planId,
        CancellationToken cancellationToken = default);
}

public interface IPhysicalPilotConfirmationService
{
    Task<PhysicalPilotConfirmationResult> ConfirmAsync(
        PhysicalPilotConfirmationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPilotConfirmationStore
{
    Task SaveAsync(
        PhysicalPilotConfirmationRecord record,
        CancellationToken cancellationToken = default);

    Task<PhysicalPilotConfirmationRecord?> GetLatestForPlanAsync(
        Guid planId,
        CancellationToken cancellationToken = default);
}

public interface IPhysicalPilotRecoveryGuide
{
    string GetMarkdown();

    Task ExportAsync(string outputPath, CancellationToken cancellationToken = default);
}
