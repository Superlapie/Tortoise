using System.Text.Json;
using System.Text.Json.Serialization;
using Tortoise.Core.Recovery;
using Tortoise.Persistence.Diagnostics;

namespace Tortoise.Persistence.Recovery;

public sealed class RecoveryManifestExporter : IRecoveryManifestExporter
{
    public async Task ExportAsync(
        RecoveryPreparationRecord record,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var document = CreateDocument(record);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);
    }

    internal static RecoveryManifestDocument CreateDocument(RecoveryPreparationRecord record)
    {
        var identity = record.Snapshot.BeforeDevice.Identity;
        var health = record.Snapshot.BeforeDevice.Health;
        var driver = record.Snapshot.BeforeDriver.Package.Identity;
        var proposed = record.Plan.ProposedUpdate;

        return new RecoveryManifestDocument(
            SchemaVersion: 1,
            ExportedAtUtc: DateTimeOffset.UtcNow,
            Preparation: new RecoveryManifestHeader(
                record.PreparationId,
                record.PlanId,
                record.PreparedAtUtc),
            Plan: new RecoveryManifestPlanSection(
                record.Plan.PlanHash,
                record.Plan.ProposedUpdate.Classification.ToString(),
                record.Plan.ProposedUpdate.RiskLevel.ToString(),
                record.Plan.RestartExpected),
            BeforeDevice: new RecoveryManifestDeviceSection(
                RedactionHelper.Redact(identity.DeviceInstanceId),
                RedactionHelper.Redact(identity.FriendlyName),
                identity.ClassName,
                identity.Manufacturer,
                health.State.ToString()),
            BeforeDriver: new RecoveryManifestDriverSection(
                driver.ProviderName,
                driver.DriverVersion.ToString(),
                driver.PublishedInfName),
            ProposedUpdate: new RecoveryManifestProposedUpdateSection(
                RedactionHelper.Redact(proposed.Candidate.Package.Identity.PublishedInfName),
                proposed.Candidate.UpdateIdentity,
                proposed.Candidate.Package.Source.DisplayName,
                proposed.RestartRequired),
            System: new RecoveryManifestSystemSection(
                RedactionHelper.Redact(record.SystemSnapshot.WindowsVersion),
                record.SystemSnapshot.Architecture,
                record.SystemSnapshot.PendingReboot),
            PackageExport: new RecoveryManifestExportSection(
                record.ExportAttempt.ExportServiceEnabled,
                record.ExportAttempt.Succeeded,
                record.ExportAttempt.ExportedPackagePath is null
                    ? null
                    : RedactionHelper.Redact(record.ExportAttempt.ExportedPackagePath),
                RedactionHelper.Redact(record.ExportAttempt.Message),
                record.ExportAttempt.MatchedPackageName),
            SystemRestore: new RecoveryManifestSystemRestoreSection(
                record.SystemRestore.IsEnabled,
                RedactionHelper.Redact(record.SystemRestore.StatusMessage)),
            RecoverySteps:
            [
                "Review the before-driver metadata captured in this manifest.",
                "If an exported driver package is available, keep it in a safe location before any update attempt.",
                "If the device becomes unstable after a future update, use Device Manager or Windows recovery options to roll back.",
                "If System Restore is enabled, consider creating or verifying a restore point before changing boot-critical drivers.",
            ],
            Limitations:
            [
                "Tortoise prepares recovery information but does not guarantee rollback.",
                "Driver package export may be unavailable during read-only development builds.",
                "This manifest is not a substitute for vendor support or Windows built-in recovery tools.",
            ]);
    }

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
