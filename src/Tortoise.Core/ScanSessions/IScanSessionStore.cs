using Tortoise.Core.Recommendations;

namespace Tortoise.Core.ScanSessions;

public interface IScanSessionStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<ScanSessionRecord> SaveAsync(
        RecommendationScanResult result,
        CancellationToken cancellationToken = default);

    Task<ScanSessionRecord?> GetLatestAsync(CancellationToken cancellationToken = default);

    Task<ScanSessionRecord?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScanSessionRecord>> ListAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);
}
