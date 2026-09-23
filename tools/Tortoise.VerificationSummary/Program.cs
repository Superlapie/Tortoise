using System.Xml.Linq;

namespace Tortoise.VerificationSummary;

internal sealed record TrxFileSummary(
    string Path,
    int Total,
    int Passed,
    int Failed,
    int Skipped);

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
    int? IntegrationTestCount,
    DateTimeOffset Timestamp,
    IReadOnlyList<string> TrxFiles,
    IReadOnlyList<TrxFileSummary> TrxFileSummaries,
    IReadOnlyList<string> ValidationErrors);

internal static class Program
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
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
        var minTrxFiles = ParseInt(GetOption(args, "--min-trx-files")) ?? 2;

        if (!Directory.Exists(trxRoot))
        {
            Console.Error.WriteLine($"TRX root not found: {trxRoot}");
            return 1;
        }

        var trxFiles = Directory.GetFiles(trxRoot, "*.trx", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var counters = new TestCounters();
        var trxSummaries = new List<TrxFileSummary>();
        foreach (var trxFile in trxFiles)
        {
            var fileCounters = new TestCounters();
            MergeTrx(trxFile, fileCounters);
            MergeCounters(fileCounters, counters);
            trxSummaries.Add(new TrxFileSummary(
                trxFile,
                fileCounters.Total,
                fileCounters.Passed,
                fileCounters.Failed,
                fileCounters.Skipped));
        }

        var validationErrors = ValidateAggregation(trxFiles, counters, minTrxFiles);

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
            trxFiles.Count,
            counters.IntegrationTests,
            DateTimeOffset.UtcNow,
            trxFiles,
            trxSummaries,
            validationErrors);

        var jsonPath = Path.Combine(outputRoot, "summary.json");
        File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(payload, JsonOptions));

        var md = BuildMarkdown(payload);
        var mdPath = Path.Combine(outputRoot, "summary.md");
        File.WriteAllText(mdPath, md);

        Console.WriteLine($"Wrote {jsonPath}");
        Console.WriteLine($"Wrote {mdPath}");

        if (validationErrors.Count > 0)
        {
            foreach (var error in validationErrors)
            {
                Console.Error.WriteLine($"VALIDATION ERROR: {error}");
            }

            return 3;
        }

        return counters.Failed > 0 ? 2 : 0;
    }

    private static IReadOnlyList<string> ValidateAggregation(
        IReadOnlyList<string> trxFiles,
        TestCounters counters,
        int minTrxFiles)
    {
        var errors = new List<string>();

        if (trxFiles.Count < minTrxFiles)
        {
            errors.Add($"Expected at least {minTrxFiles} TRX files, found {trxFiles.Count}.");
        }

        if (trxFiles.Count == 0)
        {
            errors.Add("No TRX files were found; verification summary cannot represent the test suite.");
            return errors;
        }

        if (counters.Total <= 0)
        {
            errors.Add("Aggregated TRX total is zero; no tests were recorded.");
        }

        if (counters.Failed > 0)
        {
            errors.Add($"Aggregated TRX reports {counters.Failed} failed test(s).");
        }

        var duplicatePaths = trxFiles
            .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicatePaths.Count > 0)
        {
            errors.Add($"Duplicate TRX paths detected: {string.Join(", ", duplicatePaths)}");
        }

        return errors;
    }

    private static void MergeCounters(TestCounters source, TestCounters destination)
    {
        destination.Total += source.Total;
        destination.Passed += source.Passed;
        destination.Failed += source.Failed;
        destination.Skipped += source.Skipped;
        destination.IntegrationTests += source.IntegrationTests;
    }

    private static void MergeTrx(string trxFile, TestCounters counters)
    {
        var doc = XDocument.Load(trxFile);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var traitsByTestId = doc.Descendants(ns + "UnitTest")
            .Select(element => new
            {
                Id = element.Attribute("id")?.Value,
                Traits = element.Descendants(ns + "Trait")
                    .Select(trait => (
                        Name: trait.Attribute("name")?.Value,
                        Value: trait.Attribute("value")?.Value))
                    .ToList(),
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .ToDictionary(entry => entry.Id!, entry => entry.Traits);

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

            var testId = result.Attribute("testId")?.Value;
            if (testId is not null
                && traitsByTestId.TryGetValue(testId, out var traits)
                && traits.Any(trait =>
                    string.Equals(trait.Name, "Category", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(trait.Value, "Integration", StringComparison.OrdinalIgnoreCase)))
            {
                counters.IntegrationTests++;
            }
        }
    }

    private static string BuildMarkdown(VerificationSummaryPayload payload)
    {
        var builder = new System.Text.StringBuilder();
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
        foreach (var summary in payload.TrxFileSummaries)
        {
            builder.AppendLine($"  - `{Path.GetFileName(summary.Path)}`: total={summary.Total}, passed={summary.Passed}, failed={summary.Failed}, skipped={summary.Skipped}");
        }

        if (payload.IntegrationTestCount is not null)
        {
            builder.AppendLine($"- Integration tests (TRX Category trait): {payload.IntegrationTestCount}");
        }

        if (payload.ValidationErrors.Count > 0)
        {
            builder.AppendLine("- Validation errors:");
            foreach (var error in payload.ValidationErrors)
            {
                builder.AppendLine($"  - {error}");
            }
        }

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
