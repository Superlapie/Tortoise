namespace Tortoise.Core.Drivers;

public sealed record DriverStorePackage(
    string PublishedInfName,
    string OriginalInfName,
    bool IsInbox,
    string ClassName,
    Guid ClassGuid,
    string ProviderName,
    Version DriverVersion,
    DateOnly? DriverDate,
    string PackageIdentifier);

public enum DriverAssociationKind
{
    Active = 0,
    StoreOnly = 1,
}

public sealed record DeviceDriverAssociation(
    string DeviceInstanceId,
    DriverStorePackage Package,
    DriverAssociationKind AssociationKind);

public sealed record DriverPackageInventoryResult(
    IReadOnlyList<DriverStorePackage> StorePackages,
    IReadOnlyList<DeviceDriverAssociation> Associations,
    IReadOnlyList<string> Warnings,
    DateTimeOffset ScannedAtUtc);

public sealed record DriverPackageExportResult(
    bool Succeeded,
    string? ExportedPath,
    string Message);
