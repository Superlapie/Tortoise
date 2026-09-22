using Tortoise.Core.Recommendations;
using Tortoise.Core.ScanSessions;

namespace Tortoise.App.Services;

public sealed class ScanSessionSnapshot
{
    public required long SessionId { get; init; }

    public required DateTimeOffset ScannedAtUtc { get; init; }

    public required RecommendationScanResult Result { get; init; }
}

public sealed class AppSessionState
{
    private readonly IScanSessionStore _store;
    private readonly List<ScanSessionSnapshot> _history = [];

    public AppSessionState(IScanSessionStore store)
    {
        _store = store;
    }

    public ScanSessionSnapshot? LastScan { get; private set; }

    public IReadOnlyList<ScanSessionSnapshot> History => _history;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _store.InitializeAsync(cancellationToken);
        var records = await _store.ListAsync(cancellationToken: cancellationToken);

        _history.Clear();
        foreach (var record in records)
        {
            _history.Add(ToSnapshot(record));
        }

        LastScan = _history.FirstOrDefault();
    }

    public async Task RecordScanAsync(
        RecommendationScanResult result,
        CancellationToken cancellationToken = default)
    {
        var record = await _store.SaveAsync(result, cancellationToken);
        var snapshot = ToSnapshot(record);
        LastScan = snapshot;
        _history.Insert(0, snapshot);
    }

    private static ScanSessionSnapshot ToSnapshot(ScanSessionRecord record) =>
        new()
        {
            SessionId = record.Id,
            ScannedAtUtc = record.ScannedAtUtc,
            Result = record.Result,
        };
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
        await _sessionState.RecordScanAsync(result, cancellationToken);
        return result;
    }
}
