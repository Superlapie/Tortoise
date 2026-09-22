namespace Tortoise.Core.Drivers;

public interface IDriverPackageExportService
{
    bool IsExportEnabled { get; }

    string DisabledReason { get; }

    Task<DriverPackageExportResult> ExportAsync(
        DriverStorePackage package,
        string destinationDirectory,
        CancellationToken cancellationToken = default);
}
