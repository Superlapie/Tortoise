using Microsoft.Extensions.DependencyInjection;
using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Windows.Extensions;

if (args.Length == 0)
{
    PrintUsage();
    return 0;
}

var command = args[0].ToLowerInvariant();
return command switch
{
    "scan" or "devices" => await RunDeviceScanAsync(args),
    "packages" or "drivers" => await RunPackageScanAsync(args),
    "status" => RunStatus(),
    _ => PrintUnknown(command),
};

static int PrintUsage()
{
    Console.WriteLine("Tortoise CLI (read-only)");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  tortoise scan");
    Console.WriteLine("  tortoise devices [--problem]");
    Console.WriteLine("  tortoise packages");
    Console.WriteLine("  tortoise status");
    return 0;
}

static int PrintUnknown(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return 1;
}

static int RunStatus()
{
    Console.WriteLine($"Mutation enabled: {MutationCapability.ReadOnly.IsEnabled}");
    Console.WriteLine($"Environment: {MutationCapability.ReadOnly.Environment}");
    Console.WriteLine($"Platform: {(OperatingSystem.IsWindows() ? "Windows" : "Unsupported for device scan")}");
    return 0;
}

static async Task<int> RunDeviceScanAsync(string[] args)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Device inventory requires Windows.");
        return 1;
    }

    var showProblemsOnly = args.Contains("--problem", StringComparer.OrdinalIgnoreCase);
    var services = new ServiceCollection();
    services.AddTortoiseWindowsDeviceInventory();
    var provider = services.BuildServiceProvider().GetRequiredService<IDeviceInventoryProvider>();
    var result = await provider.ScanAsync();

    var devices = result.Devices.AsEnumerable();
    if (showProblemsOnly)
    {
        devices = devices.Where(entry => entry.Snapshot.Health.State == DeviceHealthState.Problem);
    }

    foreach (var entry in devices.OrderBy(entry => entry.Snapshot.Identity.ClassName))
    {
        var identity = entry.Snapshot.Identity;
        var health = entry.Snapshot.Health;
        Console.WriteLine($"{identity.FriendlyName}");
        Console.WriteLine($"  Class: {identity.ClassName}");
        Console.WriteLine($"  Instance ID: {identity.DeviceInstanceId}");
        Console.WriteLine($"  Status: {health.State}");
        if (health.ProblemCode is not null)
        {
            Console.WriteLine($"  Problem code: {health.ProblemCode}");
        }

        if (entry.InstalledDriver is not null)
        {
            Console.WriteLine($"  Driver: {entry.InstalledDriver.ProviderName} {entry.InstalledDriver.DriverVersion}");
        }

        Console.WriteLine();
    }

    Console.WriteLine($"Devices: {result.Devices.Count}");
    if (result.Warnings.Count > 0)
    {
        Console.WriteLine($"Warnings: {result.Warnings.Count}");
    }

    return 0;
}

static async Task<int> RunPackageScanAsync(string[] args)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Driver package inventory requires Windows.");
        return 1;
    }

    var services = new ServiceCollection();
    services.AddTortoiseWindowsDriverPackageInventory();
    var provider = services.BuildServiceProvider().GetRequiredService<IDriverPackageInventoryProvider>();
    var result = await provider.ScanAsync();

    foreach (var package in result.StorePackages.OrderBy(package => package.ClassName).ThenBy(package => package.ProviderName))
    {
        Console.WriteLine($"{package.PublishedInfName}");
        Console.WriteLine($"  Provider: {package.ProviderName}");
        Console.WriteLine($"  Class: {package.ClassName}");
        Console.WriteLine($"  Version: {package.DriverVersion}");
        Console.WriteLine($"  Inbox: {package.IsInbox}");
        Console.WriteLine();
    }

    Console.WriteLine($"Store packages: {result.StorePackages.Count}");
    Console.WriteLine($"Active associations: {result.Associations.Count}");
    if (result.Warnings.Count > 0)
    {
        Console.WriteLine($"Warnings: {result.Warnings.Count}");
    }

    return 0;
}
