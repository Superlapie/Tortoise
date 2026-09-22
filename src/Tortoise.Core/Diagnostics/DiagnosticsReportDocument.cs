namespace Tortoise.Core.Diagnostics;

public sealed record DiagnosticsReportDocument(
    int SchemaVersion,
    DateTimeOffset ExportedAtUtc,
    DiagnosticsSessionHeader Session,
    DiagnosticsSummary Summary,
    DiagnosticsPolicySection UpdatePolicy,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<DiagnosticsRecommendationEntry> Recommendations,
    IReadOnlyList<DiagnosticsUnmatchedUpdateEntry> UnmatchedUpdates);

public sealed record DiagnosticsSessionHeader(
    long Id,
    DateTimeOffset ScannedAtUtc);

public sealed record DiagnosticsSummary(
    int DeviceCount,
    int ProblemCount,
    int RecommendedCount,
    int OptionalCount,
    int RestrictedCount,
    int UnmatchedUpdateCount,
    int WarningCount);

public sealed record DiagnosticsPolicySection(
    bool IsManaged,
    string SourceDescription,
    string DisplayMessage);

public sealed record DiagnosticsRecommendationEntry(
    string DeviceInstanceId,
    string FriendlyName,
    string ClassName,
    string Manufacturer,
    string HealthState,
    int? ProblemCode,
    string Classification,
    string RiskLevel,
    string Summary,
    string Explanation,
    string? CurrentDriverProvider,
    string? CurrentDriverVersion,
    string? ProposedUpdateTitle,
    string? ProposedUpdateId,
    bool RestartRequired);

public sealed record DiagnosticsUnmatchedUpdateEntry(
    string UpdateId,
    int Revision,
    string Title,
    string Classification);
