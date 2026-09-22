using Tortoise.Contracts.Elevation;
using Tortoise.Core.Planning;
using Tortoise.Broker.Ipc;
using Tortoise.Security.Broker;

namespace Tortoise.Broker.Validation;

public sealed class BrokerPlanValidationClient : IBrokerPlanValidationClient
{
    private readonly BrokerPipeClient _client = new();

    public async Task<BrokerPlanValidationResult> ValidatePlanAsync(
        Guid planId,
        string planHash,
        BrokerPlanValidationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var hostOptions = new BrokerHostOptions
        {
            SessionId = options.SessionId,
            CapabilityToken = options.CapabilityToken,
            PipeName = options.PipeName,
            ConnectTimeoutMs = options.ConnectTimeoutMs,
        };

        var response = await _client.SendAsync(
            hostOptions,
            BrokerRequestFactory.Create(
                BrokerOperation.ValidatePlan,
                options.SessionId,
                options.CapabilityToken,
                planId,
                planHash),
            cancellationToken);

        return new BrokerPlanValidationResult(
            response.Succeeded,
            response.Message,
            response.ErrorCode.ToString());
    }
}
