using Tortoise.Core.Devices;
using Tortoise.Core.Recommendations;
using Tortoise.Core.ScanSessions;
using Tortoise.Core.Updates;

namespace Tortoise.Persistence.ScanSessions;

internal static class ScanSessionSummaryCalculator
{
    public static ScanSessionSummary Calculate(RecommendationScanResult result) =>
        new(
            result.Recommendations.Count,
            result.Recommendations.Count(recommendation =>
                recommendation.Device.Snapshot.Health.State == DeviceHealthState.Problem),
            result.Recommendations.Count(recommendation =>
                recommendation.Classification == UpdateClassification.WindowsRecommended),
            result.Recommendations.Count(recommendation =>
                recommendation.Classification == UpdateClassification.WindowsOptional),
            result.Recommendations.Count(recommendation =>
                recommendation.Classification == UpdateClassification.Restricted),
            result.UnmatchedUpdates.Count,
            result.Warnings.Count);
}
