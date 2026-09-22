namespace Tortoise.Core.Diagnostics;

public interface IDiagnosticsReportExporter
{
    Task ExportSessionAsync(
        long sessionId,
        string outputPath,
        CancellationToken cancellationToken = default);

    Task ExportLatestSessionAsync(
        string outputPath,
        CancellationToken cancellationToken = default);
}
