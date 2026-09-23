using System.Text.Json;
using System.Text.Json.Serialization;
using Tortoise.Core.Diagnostics;
using Tortoise.Core.Recommendations;
using Tortoise.Core.ScanSessions;
using Tortoise.Core.Updates;

namespace Tortoise.Persistence.Diagnostics;

public sealed class DiagnosticsReportExporter : IDiagnosticsReportExporter
{
    private readonly IScanSessionStore _scanSessionStore;

    public DiagnosticsReportExporter(IScanSessionStore scanSessionStore)
    {
        _scanSessionStore = scanSessionStore;
    }

    public async Task ExportSessionAsync(
        long sessionId,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var session = await _scanSessionStore.GetByIdAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Scan session {sessionId.ToString()} was not found.");

        await WriteReportAsync(session, outputPath, cancellationToken);
    }

    public async Task ExportLatestSessionAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var session = await _scanSessionStore.GetLatestAsync(cancellationToken)
            ?? throw new InvalidOperationException("No scan sessions are available to export.");

        await WriteReportAsync(session, outputPath, cancellationToken);
    }

    private static async Task WriteReportAsync(
        ScanSessionRecord session,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var document = CreateDocument(session);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);
    }

    public static DiagnosticsReportDocument CreateDocument(ScanSessionRecord session)
    {
        var result = session.Result;
        var summary = session.Summary;

        return new DiagnosticsReportDocument(
            SchemaVersion: 1,
            ExportedAtUtc: DateTimeOffset.UtcNow,
            Session: new DiagnosticsSessionHeader(session.Id, session.ScannedAtUtc),
            Summary: new DiagnosticsSummary(
                summary.DeviceCount,
                summary.ProblemCount,
                summary.RecommendedCount,
                summary.OptionalCount,
                summary.RestrictedCount,
                summary.UnmatchedUpdateCount,
                summary.WarningCount),
            UpdatePolicy: new DiagnosticsPolicySection(
                result.UpdatePolicy.IsManaged,
                RedactionHelper.Redact(result.UpdatePolicy.SourceDescription),
                RedactionHelper.Redact(result.UpdatePolicy.DisplayMessage)),
            Warnings: result.Warnings.Select(RedactionHelper.Redact).ToList(),
            Recommendations: result.Recommendations.Select(CreateRecommendationEntry).ToList(),
            UnmatchedUpdates: result.UnmatchedUpdates.Select(CreateUnmatchedUpdateEntry).ToList());
    }

    private static DiagnosticsRecommendationEntry CreateRecommendationEntry(DeviceUpdateRecommendation recommendation)
    {
        var identity = recommendation.Device.Snapshot.Identity;
        var health = recommendation.Device.Snapshot.Health;
        var binding = recommendation.Device.InstalledDriver;

        return new DiagnosticsRecommendationEntry(
            RedactionHelper.Redact(identity.DeviceInstanceId),
            RedactionHelper.Redact(identity.FriendlyName),
            identity.ClassName,
            identity.Manufacturer,
            health.State.ToString(),
            health.ProblemCode,
            recommendation.Classification.ToString(),
            recommendation.RiskLevel.ToString(),
            RedactionHelper.Redact(recommendation.Summary),
            RedactionHelper.Redact(recommendation.Explanation),
            binding?.ProviderName,
            binding?.DriverVersion?.ToString(),
            recommendation.ApplicableUpdate is null
                ? null
                : RedactionHelper.Redact(recommendation.ApplicableUpdate.Title),
            recommendation.ApplicableUpdate?.UpdateId,
            recommendation.ApplicableUpdate?.RestartRequired ?? false);
    }

    private static DiagnosticsUnmatchedUpdateEntry CreateUnmatchedUpdateEntry(WindowsUpdateCandidate candidate) =>
        new(
            candidate.UpdateId,
            candidate.Revision,
            RedactionHelper.Redact(candidate.Title),
            candidate.Classification.ToString());

    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
