using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Tortoise.VerificationSummary;

internal sealed record VerificationSummaryPayload(
    string Sha,
    string Os,
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    int? BuildWarnings,
    int? BuildErrors,
    int TestProjectCount,
    int IntegrationTestCount,
    DateTimeOffset Timestamp,
    IReadOnlyList<string> TrxFiles);

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Main(string[] args)
    {
        var trxRoot = GetOption(args, "--trx-root") ?? "artifacts/test-results";
        var outputRoot = GetOption(args, "--output-root") ?? "artifacts/verification";
        var sha = GetOption(args, "--sha")
            ?? Environment.GetEnvironmentVariable("GITHUB_SHA")
            ?? "unknown";
        var os = GetOption(args, "--os")
            ?? Environment.GetEnvironmentVariable("RUNNER_OS")
            ?? Environment.OSVersion.Platform.ToString();

        if (!Directory.Exists(trxRoot))
        {
            Console.Error.WriteLine($"TRX root not found: {trxRoot}");
            return 1;
        }

        var trxFiles = Directory.GetFiles(trxRoot, "*.trx", SearchOption.AllDirectories);
        var counters = new TestCounters();
        foreach (var trxFile in trxFiles)
        {
            MergeTrx(trxFile, counters);
        }

        Directory.CreateDirectory(outputRoot);
        var payload = new VerificationSummaryPayload(
            sha,
            os,
            counters.Total,
            counters.Passed,
            counters.Failed,
            counters.Skipped,
            ParseInt(GetOption(args, "--build-warnings")),
            ParseInt(GetOption(args, "--build-errors")),
            trxFiles.Select(path => Path.GetFileName(path)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            counters.IntegrationTests,
            DateTimeOffset.UtcNow,
            trxFiles.Select(Path.GetFullPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList());

        var jsonPath = Path.Combine(outputRoot, "summary.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, JsonOptions));

        var md = BuildMarkdown(payload);
        var mdPath = Path.Combine(outputRoot, "summary.md");
        File.WriteAllText(mdPath, md);

        Console.WriteLine($"Wrote {jsonPath}");
        Console.WriteLine($"Wrote {mdPath}");
        return counters.Failed > 0 ? 2 : 0;
    }

    private static void MergeTrx(string trxFile, TestCounters counters)
    {
        var doc = XDocument.Load(trxFile);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var results = doc.Descendants(ns + "UnitTestResult").ToList();
        foreach (var result in results)
        {
            counters.Total++;
            var outcome = result.Attribute("outcome")?.Value ?? "Unknown";
            switch (outcome)
            {
                case "Passed":
                    counters.Passed++;
                    break;
                case "Failed":
                    counters.Failed++;
                    break;
                default:
                    counters.Skipped++;
                    break;
            }

            var testName = result.Attribute("testName")?.Value ?? string.Empty;
            if (testName.Contains("Integration", StringComparison.OrdinalIgnoreCase))
            {
                counters.IntegrationTests++;
            }
        }
    }

    private static string BuildMarkdown(VerificationSummaryPayload payload)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Verification Summary");
        builder.AppendLine();
        builder.AppendLine($"- SHA: `{payload.Sha}`");
        builder.AppendLine($"- OS: `{payload.Os}`");
        builder.AppendLine($"- Timestamp (UTC): `{payload.Timestamp:o}`");
        builder.AppendLine($"- Total: {payload.Total}");
        builder.AppendLine($"- Passed: {payload.Passed}");
        builder.AppendLine($"- Failed: {payload.Failed}");
        builder.AppendLine($"- Skipped: {payload.Skipped}");
        if (payload.BuildWarnings is not null)
        {
            builder.AppendLine($"- Build warnings: {payload.BuildWarnings}");
        }

        if (payload.BuildErrors is not null)
        {
            builder.AppendLine($"- Build errors: {payload.BuildErrors}");
        }

        builder.AppendLine($"- TRX files: {payload.TrxFiles.Count}");
        builder.AppendLine($"- Integration tests (name heuristic): {payload.IntegrationTestCount}");
        return builder.ToString();
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var parsed) ? parsed : null;

    private static string? GetOption(string[] args, string name)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
            {
                return arg[(name.Length + 1)..];
            }
        }

        return null;
    }

    private sealed class TestCounters
    {
        public int Total { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public int IntegrationTests { get; set; }
    }
}
