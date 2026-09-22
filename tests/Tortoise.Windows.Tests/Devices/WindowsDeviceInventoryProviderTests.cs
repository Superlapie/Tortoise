using Tortoise.Core.Devices;
using Tortoise.Core.Errors;
using Tortoise.Windows.Devices;

namespace Tortoise.Windows.Tests.Devices;

public sealed class WindowsDeviceInventoryProviderTests
{
    [Fact]
    public async Task ScanAsync_throws_on_non_windows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var provider = new WindowsDeviceInventoryProvider();

        var exception = await Assert.ThrowsAsync<TortoiseException>(() => provider.ScanAsync());

        Assert.Equal(Tortoise.Core.Errors.TortoiseErrorCategory.InteropError, exception.Category);
    }

    [Fact]
    public async Task ScanAsync_returns_present_devices_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var provider = new WindowsDeviceInventoryProvider();
        var result = await provider.ScanAsync();

        Assert.NotEmpty(result.Devices);
        Assert.All(result.Devices, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Snapshot.Identity.DeviceInstanceId));
            Assert.NotEqual(Guid.Empty, entry.Snapshot.Identity.ClassGuid);
            Assert.True(entry.Snapshot.Identity.IsPresent);
        });
    }

    [Fact]
    public async Task ScanAsync_does_not_throw_when_individual_device_disappears()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var provider = new WindowsDeviceInventoryProvider();
        var result = await provider.ScanAsync();

        Assert.NotNull(result.Warnings);
    }
}
