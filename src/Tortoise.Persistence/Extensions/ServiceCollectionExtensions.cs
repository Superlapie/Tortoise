using Microsoft.Extensions.DependencyInjection;
using Tortoise.Core.Diagnostics;
using Tortoise.Core.ScanSessions;
using Tortoise.Persistence.Diagnostics;
using Tortoise.Persistence.Options;
using Tortoise.Persistence.ScanSessions;

namespace Tortoise.Persistence.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTortoisePersistence(
        this IServiceCollection services,
        Action<TortoisePersistenceOptions>? configure = null)
    {
        services.AddSingleton(provider =>
        {
            var options = new TortoisePersistenceOptions();
            configure?.Invoke(options);
            return options;
        });
        services.AddSingleton<IScanSessionStore, SqliteScanSessionStore>();
        services.AddSingleton<IDiagnosticsReportExporter, DiagnosticsReportExporter>();
        return services;
    }
}
