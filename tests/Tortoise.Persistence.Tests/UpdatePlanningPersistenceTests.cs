using Microsoft.Extensions.DependencyInjection;
using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Planning;
using Tortoise.Core.Recommendations;
using Tortoise.Core.Transactions;
using Tortoise.Core.Updates;
using Tortoise.Persistence.Extensions;

namespace Tortoise.Persistence.Tests;

public sealed class UpdatePlanningPersistenceTests : IDisposable
{
    private readonly string _databasePath;
    private readonly ServiceProvider _services;

    public UpdatePlanningPersistenceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"tortoise-plan-{Guid.NewGuid():N}.db");
        var collection = new ServiceCollection();
        collection.AddTortoisePersistence(options => options.DatabasePath = _databasePath);
        _services = collection.BuildServiceProvider();
    }

    [Fact]
    public async Task Update_plan_and_transaction_stores_round_trip()
    {
        var planStore = _services.GetRequiredService<IUpdatePlanStore>();
        var transactionStore = _services.GetRequiredService<IUpdateTransactionStore>();

        var storedPlan = CreateStoredPlan();
        await planStore.SavePlansAsync([storedPlan]);

        var loadedPlan = await planStore.GetPlanAsync(storedPlan.PlanId);
        Assert.NotNull(loadedPlan);
        Assert.Equal(storedPlan.Plan.PlanHash, loadedPlan!.Plan.PlanHash);

        var transactionId = Guid.NewGuid();
        var transaction = new UpdateTransactionRecord(
            new UpdateTransaction(
                transactionId,
                storedPlan.PlanId,
                UpdateTransactionState.AwaitingConsent,
                DateTimeOffset.UtcNow),
            [
                new OperationJournalEntry(
                    transactionId,
                    UpdateTransactionState.Planned,
                    UpdateTransactionState.PreflightRunning,
                    DateTimeOffset.UtcNow,
                    "Preflight checks started."),
            ]);

        await transactionStore.SaveAsync(transaction);
        var latest = await transactionStore.GetLatestForPlanAsync(storedPlan.PlanId);

        Assert.NotNull(latest);
        Assert.Equal(UpdateTransactionState.AwaitingConsent, latest!.Transaction.State);
        Assert.Single(latest.Journal);
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
        var device = new DeviceInventoryEntry(
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
            new DeviceDriverBinding("Intel", new Version(1, 0), null, "intel.inf"));

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
}
