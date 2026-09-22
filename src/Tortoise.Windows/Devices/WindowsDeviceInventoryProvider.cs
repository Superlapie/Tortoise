using System.Runtime.Versioning;
using Tortoise.Core.Devices;
using Tortoise.Core.Errors;

namespace Tortoise.Windows.Devices;

[SupportedOSPlatform("windows")]
public sealed class WindowsDeviceInventoryProvider : IDeviceInventoryProvider
{
    public Task<DeviceInventoryScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new TortoiseException(
                TortoiseErrorCategory.InteropError,
                "Device inventory is only supported on Windows.",
                "WindowsDeviceInventoryProvider was invoked on a non-Windows operating system.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<string>();
        var entries = new List<DeviceInventoryEntry>();

        try
        {
            var instanceIds = DevicePropertyReader.EnumeratePresentDeviceInstanceIds();
            using var infoSet = DeviceInfoSetScope.Create();

            foreach (var instanceId in instanceIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (DevicePropertyReader.TryReadEntry(instanceId, infoSet, out var entry, out var warning))
                {
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(warning))
                {
                    warnings.Add(warning);
                }
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
                "Unable to complete the device scan.",
                ex.Message,
                innerException: ex);
        }

        return Task.FromResult(new DeviceInventoryScanResult(entries, warnings, DateTimeOffset.UtcNow));
    }
}
