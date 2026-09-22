using Tortoise.Core.Devices;

namespace Tortoise.Core.Drivers;

public interface IDriverPackageInventoryProvider
{
    Task<DriverPackageInventoryResult> ScanAsync(
        IReadOnlyList<DeviceInventoryEntry>? devices = null,
        CancellationToken cancellationToken = default);
}
