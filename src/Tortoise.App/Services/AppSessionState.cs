using Tortoise.Core.Recommendations;

namespace Tortoise.App.Services;

public sealed class ScanSessionSnapshot
{
    public required DateTimeOffset ScannedAtUtc { get; init; }

    public required RecommendationScanResult Result { get; init; }
}

public sealed class AppSessionState
{
    private readonly List<ScanSessionSnapshot> _history = [];

    public ScanSessionSnapshot? LastScan { get; private set; }

    public IReadOnlyList<ScanSessionSnapshot> History => _history;

    public void RecordScan(RecommendationScanResult result)
    {
        var snapshot = new ScanSessionSnapshot
        {
            ScannedAtUtc = result.EvaluatedAtUtc,
            Result = result,
        };

        LastScan = snapshot;
        _history.Insert(0, snapshot);
    }
}

public sealed class ScanCoordinator
{
    private readonly IRecommendationScanService _recommendationScanService;
    private readonly AppSessionState _sessionState;

    public ScanCoordinator(
        IRecommendationScanService recommendationScanService,
        AppSessionState sessionState)
    {
        _recommendationScanService = recommendationScanService;
        _sessionState = sessionState;
    }

    public async Task<RecommendationScanResult> ScanAsync(
        RecommendationOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = await _recommendationScanService.ScanAsync(options, cancellationToken);
        _sessionState.RecordScan(result);
        return result;
    }
}
