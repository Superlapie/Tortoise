namespace Tortoise.Core.Recovery;

public sealed record RecoveryManifestDocument(
    int SchemaVersion,
    DateTimeOffset ExportedAtUtc,
    RecoveryManifestHeader Preparation,
    RecoveryManifestPlanSection Plan,
    RecoveryManifestDeviceSection BeforeDevice,
    RecoveryManifestDriverSection BeforeDriver,
    RecoveryManifestProposedUpdateSection ProposedUpdate,
    RecoveryManifestSystemSection System,
    RecoveryManifestExportSection PackageExport,
    RecoveryManifestSystemRestoreSection SystemRestore,
    IReadOnlyList<string> RecoverySteps,
    IReadOnlyList<string> Limitations);

public sealed record RecoveryManifestHeader(
    Guid PreparationId,
    Guid PlanId,
    DateTimeOffset PreparedAtUtc);

public sealed record RecoveryManifestPlanSection(
    string PlanHash,
    string Classification,
    string RiskLevel,
    bool RestartExpected);

public sealed record RecoveryManifestDeviceSection(
    string DeviceInstanceId,
    string FriendlyName,
    string ClassName,
    string Manufacturer,
    string HealthState);

public sealed record RecoveryManifestDriverSection(
    string ProviderName,
    string Version,
    string? InfName);

public sealed record RecoveryManifestProposedUpdateSection(
    string Title,
    string UpdateId,
    string Source,
    bool RestartRequired);

public sealed record RecoveryManifestSystemSection(
    string WindowsVersion,
    string Architecture,
    bool PendingReboot);

public sealed record RecoveryManifestExportSection(
    bool ExportServiceEnabled,
    bool Succeeded,
    string? ExportedPackagePath,
    string Message,
    string? MatchedPackageName);

public sealed record RecoveryManifestSystemRestoreSection(
    bool IsEnabled,
    string StatusMessage);

public interface IRecoveryManifestExporter
{
    Task ExportAsync(
        RecoveryPreparationRecord record,
        string outputPath,
        CancellationToken cancellationToken = default);
}
