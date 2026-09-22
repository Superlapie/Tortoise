using Microsoft.Extensions.DependencyInjection;
using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Pilot;
using Tortoise.Core.Planning;
using Tortoise.Core.Recommendations;
using Tortoise.Core.Updates;
using Tortoise.Persistence.Extensions;

namespace Tortoise.Persistence.Tests;

public sealed class PilotConfirmationPersistenceTests : IDisposable
{
    private readonly string _databasePath;
    private readonly ServiceProvider _services;

    public PilotConfirmationPersistenceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"tortoise-pilot-{Guid.NewGuid():N}.db");
        var collection = new ServiceCollection();
        collection.AddTortoisePersistence(options => options.DatabasePath = _databasePath);
        _services = collection.BuildServiceProvider();
    }

    [Fact]
    public async Task Pilot_confirmation_store_round_trips_latest_record_for_plan()
    {
        var planStore = _services.GetRequiredService<IUpdatePlanStore>();
        var confirmationStore = _services.GetRequiredService<IPilotConfirmationStore>();

        var storedPlan = CreateStoredPlan();
        await planStore.SavePlansAsync([storedPlan]);

        var record = new PhysicalPilotConfirmationRecord(
            Guid.NewGuid(),
            storedPlan.PlanId,
            DateTimeOffset.UtcNow,
            PhysicalPilotConstants.ConfirmationPhrase,
            PhysicalPilotConstants.RequiredAcknowledgementIds,
            true,
            "Physical pilot readiness confirmed.");

        await confirmationStore.SaveAsync(record);

        var loaded = await confirmationStore.GetLatestForPlanAsync(storedPlan.PlanId);
        Assert.NotNull(loaded);
        Assert.Equal(record.ConfirmationId, loaded!.ConfirmationId);
        Assert.Equal(record.PlanId, loaded.PlanId);
        Assert.Equal(record.ConfirmationPhrase, loaded.ConfirmationPhrase);
        Assert.Equal(record.AcknowledgedItemIds, loaded.AcknowledgedItemIds);
        Assert.True(loaded.ChecklistReady);
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
