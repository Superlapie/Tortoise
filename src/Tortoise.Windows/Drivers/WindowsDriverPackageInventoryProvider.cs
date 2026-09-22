using System.Runtime.Versioning;
using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Errors;

namespace Tortoise.Windows.Drivers;

[SupportedOSPlatform("windows")]
public sealed class DisabledDriverPackageExportService : IDriverPackageExportService
{
    public bool IsExportEnabled => false;

    public string DisabledReason =>
        "Driver package export is not enabled during read-only development builds.";

    public Task<DriverPackageExportResult> ExportAsync(
        DriverStorePackage package,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        return Task.FromResult(new DriverPackageExportResult(
            Succeeded: false,
            ExportedPath: null,
            Message: DisabledReason));
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsDriverPackageInventoryProvider : IDriverPackageInventoryProvider
{
    private readonly IDeviceInventoryProvider _deviceInventoryProvider;

    public WindowsDriverPackageInventoryProvider(IDeviceInventoryProvider deviceInventoryProvider)
    {
        _deviceInventoryProvider = deviceInventoryProvider;
    }

    public async Task<DriverPackageInventoryResult> ScanAsync(
        IReadOnlyList<DeviceInventoryEntry>? devices = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new TortoiseException(
                TortoiseErrorCategory.InteropError,
                "Driver package inventory is only supported on Windows.",
                "WindowsDriverPackageInventoryProvider was invoked on a non-Windows operating system.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<string>();
        IReadOnlyList<DriverStorePackage> storePackages;

        try
        {
            var output = PnPUtilProcessRunner.RunEnumDrivers(preferStructuredOutput: true, out var usedStructuredOutput);
            storePackages = PnPUtilDriverListParser.Parse(output, usedStructuredOutput);

            if (!usedStructuredOutput)
            {
                warnings.Add("PnPUtil structured CSV output was unavailable; legacy text output was parsed instead.");
            }
        }
        catch (TortoiseException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TortoiseException(
                TortoiseErrorCategory.InteropError,
                "Unable to enumerate driver packages.",
                ex.Message,
                innerException: ex);
        }

        devices ??= (await _deviceInventoryProvider.ScanAsync(cancellationToken)).Devices;
        var associations = DriverPackageAssociator.Associate(devices, storePackages);

        foreach (var device in devices)
        {
            if (device.InstalledDriver is null)
            {
                continue;
            }

            if (associations.All(association =>
                    !string.Equals(
                        association.DeviceInstanceId,
                        device.Snapshot.Identity.DeviceInstanceId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add(
                    $"No driver store package match found for {device.Snapshot.Identity.DeviceInstanceId}.");
            }
        }

        return new DriverPackageInventoryResult(
            storePackages,
            associations,
            warnings,
            DateTimeOffset.UtcNow);
    }
}
