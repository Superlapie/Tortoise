using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Tortoise.Core.Updates;

namespace Tortoise.WindowsUpdate.Extensions;

public static class ServiceCollectionExtensions
{
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddTortoiseWindowsUpdate(this IServiceCollection services)
    {
        services.AddSingleton<IDriverUpdateProvider, WindowsUpdateDriverProvider>();
        return services;
    }
}
