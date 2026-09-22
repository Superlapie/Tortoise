namespace Tortoise.Core.Updates;

public interface IDriverUpdateProvider
{
    Task<DriverUpdateScanResult> ScanAsync(CancellationToken cancellationToken = default);

    Task<WindowsUpdateCandidateDetails?> GetDetailsAsync(
        string updateId,
        int revision,
        CancellationToken cancellationToken = default);
}
