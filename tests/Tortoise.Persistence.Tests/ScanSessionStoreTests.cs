using Microsoft.Extensions.DependencyInjection;
using Tortoise.Core.Devices;
using Tortoise.Core.Diagnostics;
using Tortoise.Core.Recommendations;
using Tortoise.Core.ScanSessions;
using Tortoise.Core.Updates;
using Tortoise.Persistence.Extensions;

namespace Tortoise.Persistence.Tests;

public sealed class ScanSessionStoreTests : IDisposable
{
    private readonly string _databasePath;
    private readonly ServiceProvider _services;
    private readonly IScanSessionStore _store;

    public ScanSessionStoreTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"tortoise-test-{Guid.NewGuid():N}.db");
        var collection = new ServiceCollection();
        collection.AddTortoisePersistence(options => options.DatabasePath = _databasePath);
        _services = collection.BuildServiceProvider();
        _store = _services.GetRequiredService<IScanSessionStore>();
    }

    [Fact]
    public async Task Save_and_list_round_trip_scan_session_payload()
    {
        await _store.InitializeAsync();
        var original = CreateSampleResult();

        var saved = await _store.SaveAsync(original);
        var listed = await _store.ListAsync();

        Assert.Equal(saved.Id, listed[0].Id);
        Assert.Equal(original.Recommendations.Count, listed[0].Summary.DeviceCount);
        Assert.Equal(
            original.Recommendations[0].Device.Snapshot.Identity.DeviceInstanceId,
            listed[0].Result.Recommendations[0].Device.Snapshot.Identity.DeviceInstanceId);
        Assert.Equal(
            original.UnmatchedUpdates[0].UpdateId,
            listed[0].Result.UnmatchedUpdates[0].UpdateId);
    }

    [Fact]
    public async Task GetLatest_returns_most_recent_session()
    {
        await _store.InitializeAsync();
        var first = await _store.SaveAsync(CreateSampleResult(evaluatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5)));
        var second = await _store.SaveAsync(CreateSampleResult(evaluatedAtUtc: DateTimeOffset.UtcNow));

        var latest = await _store.GetLatestAsync();

        Assert.NotNull(latest);
        Assert.Equal(second.Id, latest!.Id);
        Assert.NotEqual(first.Id, latest.Id);
    }

    [Fact]
    public async Task GetById_returns_specific_session()
    {
        await _store.InitializeAsync();
        var saved = await _store.SaveAsync(CreateSampleResult());

        var loaded = await _store.GetByIdAsync(saved.Id);

        Assert.NotNull(loaded);
        Assert.Equal(saved.Id, loaded!.Id);
        Assert.Equal(saved.Summary.RecommendedCount, loaded.Summary.RecommendedCount);
    }

    public void Dispose()
    {
        _services.Dispose();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private static RecommendationScanResult CreateSampleResult(DateTimeOffset? evaluatedAtUtc = null)
    {
        var device = new DeviceInventoryEntry(
            new DeviceSnapshot(
                new DeviceIdentity(
                    "ROOT\\NET\\0001",
                    null,
                    Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                    "Net",
                    "Intel Adapter",
                    "Intel",
                    ["PCI\\VEN_8086&DEV_1234"],
                    [],
                    null,
                    null,
                    null,
                    null,
                    true),
                new DeviceHealth(DeviceHealthState.Healthy, null, null),
                evaluatedAtUtc ?? DateTimeOffset.UtcNow),
            new DeviceDriverBinding("Intel", new Version(1, 2, 3, 4), new DateOnly(2024, 5, 6), "intel.inf"));

        var update = new WindowsUpdateCandidate(
            "update-id",
            2,
            "Intel Network Driver",
            "Description",
            "Intel",
            "Net",
            "Intel Adapter",
            new Version(2, 0, 0, 0),
            new DateOnly(2025, 1, 15),
            UpdateClassification.WindowsRecommended,
            true,
            false,
            false,
            false,
            ["Drivers"]);

        var recommendation = new DeviceUpdateRecommendation(
            device,
            update,
            UpdateClassification.WindowsRecommended,
            DriverRiskLevel.Low,
            "Recommended by Windows",
            "Windows Update lists this package for the device.",
            evaluatedAtUtc ?? DateTimeOffset.UtcNow);

        return new RecommendationScanResult(
            [recommendation],
            [update with { UpdateId = "unmatched-id", Title = "Unmatched package" }],
            new WindowsUpdatePolicyInfo(false, "Windows Update", "Update source: Windows Update."),
            ["Sample warning"],
            evaluatedAtUtc ?? DateTimeOffset.UtcNow);
    }
}

public sealed class DiagnosticsReportExporterTests : IDisposable
{
    private readonly string _databasePath;
    private readonly string _outputDirectory;
    private readonly ServiceProvider _services;

    public DiagnosticsReportExporterTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"tortoise-test-{Guid.NewGuid():N}.db");
        _outputDirectory = Path.Combine(Path.GetTempPath(), $"tortoise-export-{Guid.NewGuid():N}");
        var collection = new ServiceCollection();
        collection.AddTortoisePersistence(options => options.DatabasePath = _databasePath);
        _services = collection.BuildServiceProvider();
    }

    [Fact]
    public async Task ExportLatestSession_writes_redacted_json_report()
    {
        var store = _services.GetRequiredService<IScanSessionStore>();
        var exporter = _services.GetRequiredService<IDiagnosticsReportExporter>();
        await store.InitializeAsync();

        var userSegment = Environment.UserName;
        var result = new RecommendationScanResult(
            [],
            [],
            new WindowsUpdatePolicyInfo(
                false,
                $"Policy for {userSegment}",
                $"Display message for {Environment.MachineName}"),
            [$"Path C:\\Users\\{userSegment}\\AppData"],
            DateTimeOffset.UtcNow);

        await store.SaveAsync(result);

        var outputPath = Path.Combine(_outputDirectory, "report.json");
        await exporter.ExportLatestSessionAsync(outputPath);

        var json = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("[REDACTED]", json, StringComparison.Ordinal);
        Assert.DoesNotContain(userSegment, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportSession_throws_when_session_is_missing()
    {
        var store = _services.GetRequiredService<IScanSessionStore>();
        var exporter = _services.GetRequiredService<IDiagnosticsReportExporter>();
        await store.InitializeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.ExportSessionAsync(999, Path.Combine(_outputDirectory, "missing.json")));
    }

    public void Dispose()
    {
        _services.Dispose();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }
}
