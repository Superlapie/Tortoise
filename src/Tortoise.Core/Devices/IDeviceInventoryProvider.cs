namespace Tortoise.Core.Devices;

public interface IDeviceInventoryProvider
{
    Task<DeviceInventoryScanResult> ScanAsync(CancellationToken cancellationToken = default);
}
