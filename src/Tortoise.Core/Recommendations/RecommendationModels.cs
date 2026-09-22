using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Recommendations;

public sealed record DeviceUpdateRecommendation(
    DeviceInventoryEntry Device,
    WindowsUpdateCandidate? ApplicableUpdate,
    UpdateClassification Classification,
    DriverRiskLevel RiskLevel,
    string Summary,
    string Explanation,
    DateTimeOffset EvaluatedAtUtc)
{
    public UpdateRecommendation ToUpdateRecommendation() =>
        new(
            Device.Snapshot.Identity.DeviceInstanceId,
            Classification,
            RiskLevel,
            Summary,
            Explanation,
            EvaluatedAtUtc);
}

public sealed record RecommendationScanResult(
    IReadOnlyList<DeviceUpdateRecommendation> Recommendations,
    IReadOnlyList<WindowsUpdateCandidate> UnmatchedUpdates,
    WindowsUpdatePolicyInfo UpdatePolicy,
    IReadOnlyList<string> Warnings,
    DateTimeOffset EvaluatedAtUtc);

public sealed record RecommendationOptions(
    bool IncludeOptionalUpdates = false,
    string? ActiveNetworkDeviceInstanceId = null,
    string? ActiveDisplayDeviceInstanceId = null);
