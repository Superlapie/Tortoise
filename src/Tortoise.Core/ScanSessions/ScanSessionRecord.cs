using Tortoise.Core.Recommendations;

namespace Tortoise.Core.ScanSessions;

public sealed record ScanSessionRecord(
    long Id,
    DateTimeOffset ScannedAtUtc,
    ScanSessionSummary Summary,
    RecommendationScanResult Result);

public sealed record ScanSessionSummary(
    int DeviceCount,
    int ProblemCount,
    int RecommendedCount,
    int OptionalCount,
    int RestrictedCount,
    int UnmatchedUpdateCount,
    int WarningCount);
