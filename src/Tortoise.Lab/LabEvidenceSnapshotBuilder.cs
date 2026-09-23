using Tortoise.Core.Devices;
using Tortoise.Core.Diagnostics;
using Tortoise.Core.Planning;
using Tortoise.Core.ScanSessions;
using Tortoise.Core.Transactions;
using Tortoise.Persistence.Diagnostics;

namespace Tortoise.Lab;

public sealed record LabEvidenceDeviceEntry(
    string DeviceInstanceId,
    string FriendlyName,
    string ClassName,
    string Manufacturer,
    string HealthState,
    int? ProblemCode,
    string? DriverProvider,
    string? DriverVersion);

public sealed record LabEvidencePlanEntry(
    Guid PlanId,
    string RiskLevel,
    string Classification,
    bool IsFrozen,
    long? ScanSessionId,
    DateTimeOffset CreatedAtUtc);

public sealed record LabEvidenceTransactionEntry(
    Guid TransactionId,
    Guid PlanId,
    string State,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<LabEvidenceJournalEntry> Journal);

public sealed record LabEvidenceJournalEntry(
    string FromState,
    string ToState,
    DateTimeOffset TimestampUtc,
    string? Message);

public sealed record LabEvidenceSnapshotJson(
    DiagnosticsReportDocument? RecommendationScan,
    IReadOnlyList<LabEvidenceDeviceEntry> Devices,
    IReadOnlyList<LabEvidencePlanEntry> Plans,
    IReadOnlyList<LabEvidenceTransactionEntry> Transactions);

public static class LabEvidenceSnapshotBuilder
{
    public static async Task<LabEvidenceSnapshotJson> BuildAsync(
        IScanSessionStore scanStore,
        IUpdatePlanService planService,
        IUpdateTransactionStore transactionStore,
        IDeviceInventoryProvider deviceInventoryProvider,
        CancellationToken cancellationToken = default)
    {
        await scanStore.InitializeAsync();

        DiagnosticsReportDocument? recommendationScan = null;
        var latestSession = await scanStore.GetLatestAsync(cancellationToken);
        if (latestSession is not null)
        {
            recommendationScan = DiagnosticsReportExporter.CreateDocument(latestSession);
        }

        var inventory = await deviceInventoryProvider.ScanAsync(cancellationToken);
        var devices = inventory.Devices
            .Select(CreateDeviceEntry)
            .ToList();

        var plans = (await planService.ListPlansAsync(cancellationToken: cancellationToken))
            .Select(plan => new LabEvidencePlanEntry(
                plan.PlanId,
                plan.RiskLevel.ToString(),
                plan.Classification.ToString(),
                plan.IsFrozen,
                plan.ScanSessionId,
                plan.CreatedAtUtc))
            .ToList();

        var transactions = (await transactionStore.ListAllAsync(cancellationToken))
            .Select(record => new LabEvidenceTransactionEntry(
                record.Transaction.TransactionId,
                record.Transaction.PlanId,
                record.Transaction.State.ToString(),
                record.Transaction.UpdatedAtUtc,
                record.Journal
                    .Select(entry => new LabEvidenceJournalEntry(
                        entry.FromState.ToString(),
                        entry.ToState.ToString(),
                        entry.TimestampUtc,
                        entry.Message))
                    .ToList()))
            .OrderByDescending(entry => entry.UpdatedAtUtc)
            .ToList();

        return new LabEvidenceSnapshotJson(recommendationScan, devices, plans, transactions);
    }

    private static LabEvidenceDeviceEntry CreateDeviceEntry(DeviceInventoryEntry device)
    {
        var identity = device.Snapshot.Identity;
        var health = device.Snapshot.Health;
        var binding = device.InstalledDriver;

        return new LabEvidenceDeviceEntry(
            identity.DeviceInstanceId,
            identity.FriendlyName,
            identity.ClassName,
            identity.Manufacturer,
            health.State.ToString(),
            health.ProblemCode,
            binding?.ProviderName,
            binding?.DriverVersion?.ToString());
    }
}
