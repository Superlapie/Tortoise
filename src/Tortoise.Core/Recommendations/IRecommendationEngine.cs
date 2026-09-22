using Tortoise.Core.Devices;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Recommendations;

public interface IRecommendationEngine
{
    RecommendationScanResult Evaluate(
        IReadOnlyList<DeviceInventoryEntry> devices,
        DriverUpdateScanResult updateScan,
        RecommendationOptions? options = null);
}

public interface IRecommendationScanService
{
    Task<RecommendationScanResult> ScanAsync(
        RecommendationOptions? options = null,
        CancellationToken cancellationToken = default);
}
