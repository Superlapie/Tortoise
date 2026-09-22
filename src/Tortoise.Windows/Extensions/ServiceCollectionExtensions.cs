using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Tortoise.Core.Devices;
using Tortoise.Windows.Devices;

namespace Tortoise.Windows.Extensions;

public static class ServiceCollectionExtensions
{
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddTortoiseWindowsDeviceInventory(this IServiceCollection services)
    {
        services.AddSingleton<IDeviceInventoryProvider, WindowsDeviceInventoryProvider>();
        return services;
    }
}
