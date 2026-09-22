using Tortoise.Core.Recommendations;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Planning;

public sealed record UpdatePlanningOptions(
    bool IncludeOptionalUpdates = false,
    string? DeviceInstanceId = null,
    long? ScanSessionId = null);

public sealed record StoredUpdatePlan(
    Guid PlanId,
    long? ScanSessionId,
    DateTimeOffset CreatedAtUtc,
    UpdateClassification Classification,
    DriverRiskLevel RiskLevel,
    bool IsFrozen,
    UpdatePlan Plan);

public sealed record PlanStalenessResult(
    Guid PlanId,
    bool IsStale,
    string Explanation);

public interface IUpdatePlanBuilder
{
    IReadOnlyList<StoredUpdatePlan> BuildPlans(
        RecommendationScanResult scanResult,
        UpdatePlanningOptions options);
}

public interface IUpdatePlanService
{
    Task<IReadOnlyList<StoredUpdatePlan>> CreatePlansFromLatestScanAsync(
        UpdatePlanningOptions options,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredUpdatePlan>> CreatePlansFromScanSessionAsync(
        long scanSessionId,
        UpdatePlanningOptions options,
        CancellationToken cancellationToken = default);

    Task<StoredUpdatePlan?> GetPlanAsync(
        Guid planId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredUpdatePlan>> ListPlansAsync(
        long? scanSessionId = null,
        CancellationToken cancellationToken = default);

    Task<PlanStalenessResult> EvaluateStalenessAsync(
        Guid planId,
        CancellationToken cancellationToken = default);
}
