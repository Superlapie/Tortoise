using Tortoise.Core.Updates;

namespace Tortoise.Core.Drivers;

public sealed record DriverIdentity(
    string PublishedInfName,
    string OriginalInfName,
    string ProviderName,
    string ClassName,
    Guid ClassGuid,
    Version DriverVersion,
    DateOnly? DriverDate,
    string? Signer,
    string? CatalogName,
    string? PackageIdentifier,
    string? MatchingDeviceId);

public sealed record DriverSignature(
    bool IsSigned,
    bool IsCatalogValid,
    string? Signer,
    string? CatalogName);

public enum DriverSourceKind
{
    Unknown = 0,
    WindowsUpdate = 1,
    Inbox = 2,
    OEM = 3,
    VendorOfficial = 4,
}

public sealed record DriverSource(
    DriverSourceKind Kind,
    string SourceId,
    string DisplayName,
    bool IsTrusted);

public sealed record DriverPackage(
    DriverIdentity Identity,
    DriverSignature Signature,
    DriverSource Source);

public sealed record InstalledDriver(
    DriverPackage Package,
    string DeviceInstanceId,
    DateTimeOffset? InstalledAtUtc);

public sealed record DriverCandidate(
    DriverPackage Package,
    string TargetDeviceInstanceId,
    string UpdateIdentity,
    int UpdateRevision);

public sealed record DriverUpdate(
    DriverCandidate Candidate,
    UpdateClassification Classification,
    DriverRiskLevel RiskLevel,
    bool RestartRequired,
    bool RequiresEula,
    string Explanation);
