using Microsoft.Extensions.DependencyInjection;
using Tortoise.Broker.Validation;
using Tortoise.Core.Planning;

namespace Tortoise.Broker.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTortoiseBrokerPlanValidation(this IServiceCollection services)
    {
        services.AddSingleton<IBrokerPlanValidationClient, BrokerPlanValidationClient>();
        return services;
    }
}
