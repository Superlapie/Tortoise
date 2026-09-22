using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Windows.Devices;
using Tortoise.Windows.Drivers;

namespace Tortoise.Windows.Extensions;

public static class ServiceCollectionExtensions
{
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddTortoiseWindowsDeviceInventory(this IServiceCollection services)
    {
        services.AddSingleton<IDeviceInventoryProvider, WindowsDeviceInventoryProvider>();
        return services;
    }

    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddTortoiseWindowsDriverPackageInventory(this IServiceCollection services)
    {
        services.AddTortoiseWindowsDeviceInventory();
        services.AddSingleton<IDriverPackageInventoryProvider, WindowsDriverPackageInventoryProvider>();
        services.AddSingleton<IDriverPackageExportService, DisabledDriverPackageExportService>();
        return services;
    }
}
