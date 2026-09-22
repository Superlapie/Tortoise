using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;

namespace Tortoise.Core.Tests.Drivers;

public sealed class DriverPackageAssociatorTests
{
    [Fact]
    public void Associate_matches_device_binding_to_store_package()
    {
        var storePackages = new[]
        {
            CreatePackage("oem42.inf", "Intel Corporation", new Version(10, 1, 2, 3)),
            CreatePackage("other.inf", "Contoso", new Version(1, 0)),
        };

        var devices = new[]
        {
            CreateDevice(
                "ROOT\\NET\\0001",
                new DeviceDriverBinding("Intel Corporation", new Version(10, 1, 2, 3), null, "oem42.inf")),
        };

        var associations = DriverPackageAssociator.Associate(devices, storePackages);

        Assert.Single(associations);
        Assert.Equal("ROOT\\NET\\0001", associations[0].DeviceInstanceId);
        Assert.Equal("oem42.inf", associations[0].Package.PublishedInfName);
        Assert.Equal(DriverAssociationKind.Active, associations[0].AssociationKind);
    }

    [Fact]
    public void Associate_skips_devices_without_confident_store_match()
    {
        var storePackages = new[]
        {
            CreatePackage("other.inf", "Intel Corporation", new Version(9, 0)),
        };

        var devices = new[]
        {
            CreateDevice(
                "ROOT\\NET\\0001",
                new DeviceDriverBinding("Intel Corporation", new Version(10, 1, 2, 3), null, "oem42.inf")),
        };

        var associations = DriverPackageAssociator.Associate(devices, storePackages);

        Assert.Empty(associations);
    }

    private static DriverStorePackage CreatePackage(string inf, string provider, Version version) =>
        new(
            inf,
            inf,
            false,
            "Net",
            Guid.Parse("00112233-4455-6677-8899-AABBCCDDEEFF"),
            provider,
            version,
            null,
            inf);

    private static DeviceInventoryEntry CreateDevice(string instanceId, DeviceDriverBinding binding)
    {
        var identity = new DeviceIdentity(
            instanceId,
            null,
            Guid.NewGuid(),
            "Net",
            "Adapter",
            binding.ProviderName,
            [],
            [],
            null,
            null,
            null,
            null,
            true);

        var snapshot = new DeviceSnapshot(
            identity,
            new DeviceHealth(DeviceHealthState.Healthy, null, null),
            DateTimeOffset.UtcNow);

        return new DeviceInventoryEntry(snapshot, binding);
    }
}
