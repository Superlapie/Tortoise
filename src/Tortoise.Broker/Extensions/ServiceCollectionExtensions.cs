using Microsoft.Extensions.DependencyInjection;
using Tortoise.Broker.Validation;
using Tortoise.Core.Installation;
using Tortoise.Core.Planning;

namespace Tortoise.Broker.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTortoiseBrokerPlanValidation(this IServiceCollection services)
    {
        services.AddSingleton<IBrokerPlanValidationClient, BrokerPlanValidationClient>();
        return services;
    }

    public static IServiceCollection AddTortoiseBrokerDriverInstall(this IServiceCollection services)
    {
        services.AddSingleton<IBrokerDriverInstallClient, BrokerDriverInstallClient>();
        return services;
    }
}
