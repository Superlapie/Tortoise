using Tortoise.Core.Devices;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Recommendations;

public sealed class RecommendationScanService : IRecommendationScanService
{
    private readonly IDeviceInventoryProvider _deviceInventoryProvider;
    private readonly IDriverUpdateProvider _driverUpdateProvider;
    private readonly IRecommendationEngine _recommendationEngine;

    public RecommendationScanService(
        IDeviceInventoryProvider deviceInventoryProvider,
        IDriverUpdateProvider driverUpdateProvider,
        IRecommendationEngine recommendationEngine)
    {
        _deviceInventoryProvider = deviceInventoryProvider;
        _driverUpdateProvider = driverUpdateProvider;
        _recommendationEngine = recommendationEngine;
    }

    public async Task<RecommendationScanResult> ScanAsync(
        RecommendationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var deviceScan = await _deviceInventoryProvider.ScanAsync(cancellationToken);
        var updateScan = await _driverUpdateProvider.ScanAsync(cancellationToken);
        return _recommendationEngine.Evaluate(deviceScan.Devices, updateScan, options);
    }
}
