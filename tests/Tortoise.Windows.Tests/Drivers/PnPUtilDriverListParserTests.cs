using Tortoise.Core.Drivers;
using Tortoise.Windows.Drivers;

namespace Tortoise.Windows.Tests.Drivers;

public sealed class PnPUtilDriverListParserTests
{
    [Fact]
    public void ParseCsv_reads_sample_fixture()
    {
        var csv = File.ReadAllText(Path.Combine(FindRepoRoot(), "testdata", "drivers", "pnputil-enum-drivers-sample.csv"));
        var packages = PnPUtilDriverListParser.ParseCsv(csv);

        Assert.Equal(2, packages.Count);
        Assert.Contains(packages, package => package.PublishedInfName == "oem42.inf");
        Assert.Contains(packages, package => package.IsInbox);
    }

    [Fact]
    public void ParseLegacyText_reads_block_format()
    {
        const string legacy = """
            Published Name:     oem42.inf
            Original Name:      oem42.inf
            Provider Name:      Intel Corporation
            Class Name:         Net
            Class GUID:         {00112233-4455-6677-8899-AABBCCDDEEFF}
            Driver Version:     10.1.2.3
            Driver Date:        6/15/2024
            Extension ID:       oem42.inf

            Published Name:     inboxaudio.inf
            Original Name:      inboxaudio.inf
            Provider Name:      Microsoft
            Class Name:         MEDIA
            Class GUID:         {AABBCCDD-EEFF-0011-2233-445566778899}
            Driver Version:     10.0.22000.1
            Driver Date:        4/1/2023
            Extension ID:       inboxaudio.inf
            """;

        var packages = PnPUtilDriverListParser.ParseLegacyText(legacy);

        Assert.Equal(2, packages.Count);
        Assert.Equal("Intel Corporation", packages[0].ProviderName);
    }

    [Fact]
    public async Task Export_service_is_disabled_during_read_only_phase()
    {
        var service = new DisabledDriverPackageExportService();
        var package = new DriverStorePackage(
            "oem42.inf",
            "oem42.inf",
            false,
            "Net",
            Guid.NewGuid(),
            "Intel Corporation",
            new Version(1, 0),
            null,
            "oem42.inf");

        var result = await service.ExportAsync(package, "C:\\Temp");

        Assert.False(result.Succeeded);
        Assert.False(service.IsExportEnabled);
    }

    [Fact]
    public async Task ScanAsync_completes_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var provider = new WindowsDriverPackageInventoryProvider(new Tortoise.Windows.Devices.WindowsDeviceInventoryProvider());
        var result = await provider.ScanAsync();

        Assert.NotNull(result.StorePackages);
        // pnputil /enum-drivers lists third-party packages only; hosted CI images often have none.
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Tortoise.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
