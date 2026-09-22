using Microsoft.Extensions.DependencyInjection;
using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Planning;
using Tortoise.Core.Recovery;
using Tortoise.Core.Recommendations;
using Tortoise.Core.Updates;
using Tortoise.Persistence.Extensions;
using Tortoise.Persistence.Recovery;

namespace Tortoise.Persistence.Tests;

public sealed class RecoveryPreparationPersistenceTests : IDisposable
{
    private readonly string _databasePath;
    private readonly ServiceProvider _services;

    public RecoveryPreparationPersistenceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"tortoise-recover-{Guid.NewGuid():N}.db");
        var collection = new ServiceCollection();
        collection.AddTortoisePersistence(options => options.DatabasePath = _databasePath);
        _services = collection.BuildServiceProvider();
    }

    [Fact]
    public async Task Recovery_preparation_store_round_trips_payload()
    {
        var planStore = _services.GetRequiredService<IUpdatePlanStore>();
        var recoveryStore = _services.GetRequiredService<IRecoveryPreparationStore>();
        var exporter = _services.GetRequiredService<IRecoveryManifestExporter>();

        var storedPlan = CreateStoredPlan();
        await planStore.SavePlansAsync([storedPlan]);

        var record = CreateRecoveryRecord(storedPlan);
        await recoveryStore.SaveAsync(record);

        var loaded = await recoveryStore.GetAsync(record.PreparationId);
        Assert.NotNull(loaded);
        Assert.Equal(record.PlanId, loaded!.PlanId);
        Assert.Equal(record.Snapshot.BeforeDriver.Package.Identity.DriverVersion, loaded.Snapshot.BeforeDriver.Package.Identity.DriverVersion);

        var outputPath = Path.Combine(Path.GetTempPath(), $"recovery-manifest-{Guid.NewGuid():N}.json");
        await exporter.ExportAsync(loaded, outputPath);
        var json = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("recoverySteps", json, StringComparison.Ordinal);
        Assert.Contains("Tortoise prepares recovery information but does not guarantee rollback.", json, StringComparison.Ordinal);

        File.Delete(outputPath);
    }

    public void Dispose()
    {
        _services.Dispose();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private static StoredUpdatePlan CreateStoredPlan()
    {
        var builder = new UpdatePlanBuilder();
        var device = CreateDevice(new Version(1, 0));
        var recommendation = new DeviceUpdateRecommendation(
            device,
            new WindowsUpdateCandidate(
                "update-id",
                1,
                "Intel Network Driver",
                null,
                "Intel",
                "Net",
                "Intel Adapter",
                new Version(2, 0),
                null,
                UpdateClassification.WindowsRecommended,
                false,
                false,
                false,
                false,
                []),
            UpdateClassification.WindowsRecommended,
            DriverRiskLevel.Low,
            "Recommended by Windows",
            "Explanation",
            DateTimeOffset.UtcNow);

        var scanResult = new RecommendationScanResult(
            [recommendation],
            [],
            new WindowsUpdatePolicyInfo(false, "Windows Update", "Update source: Windows Update."),
            [],
            DateTimeOffset.UtcNow);

        return builder.BuildPlans(scanResult, new UpdatePlanningOptions()).Single();
    }

    private static RecoveryPreparationRecord CreateRecoveryRecord(StoredUpdatePlan storedPlan)
    {
        var preparationId = Guid.NewGuid();

        return new RecoveryPreparationRecord(
            preparationId,
            storedPlan.PlanId,
            TransactionId: null,
            DateTimeOffset.UtcNow,
            new RecoverySnapshot(
                preparationId,
                storedPlan.Plan.DeviceSnapshot,
                storedPlan.Plan.CurrentDriver,
                null,
                DateTimeOffset.UtcNow),
            new SystemSnapshot("Windows Test", "X64", false, DateTimeOffset.UtcNow),
            new RecoveryExportAttempt(false, false, null, "Export disabled.", null),
            new SystemRestoreInfo(true, "Enabled"),
            storedPlan.Plan);
    }

    private static DeviceInventoryEntry CreateDevice(Version installedVersion)
    {
        return new DeviceInventoryEntry(
            new DeviceSnapshot(
                new DeviceIdentity(
                    "ROOT\\NET\\0001",
                    null,
                    Guid.NewGuid(),
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
                DateTimeOffset.UtcNow),
            new DeviceDriverBinding("Intel", installedVersion, null, "intel.inf"));
    }
}
