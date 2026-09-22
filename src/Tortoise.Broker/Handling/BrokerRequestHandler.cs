using System.Reflection;
using System.Text.Json;
using Tortoise.Contracts.Elevation;
using Tortoise.Contracts.Mutation;
using Tortoise.Security.Broker;

namespace Tortoise.Broker.Handling;

public sealed class BrokerRequestHandler
{
    private readonly BrokerRequestValidator _validator = new();
    private readonly BrokerReplayGuard _replayGuard = new();
    private readonly string _brokerVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0-alpha";

    public BrokerResponse Handle(BrokerRequest request, int expectedSessionId)
    {
        var validation = _validator.Validate(request, expectedSessionId, _replayGuard);
        if (!validation.IsValid)
        {
            return new BrokerResponse(
                request.RequestId,
                false,
                validation.ErrorCode,
                validation.Message);
        }

        return request.Operation switch
        {
            BrokerOperation.Ping => Success(request.RequestId, "Broker ping succeeded."),
            BrokerOperation.GetStatus => Success(
                request.RequestId,
                "Broker status available.",
                JsonSerializer.Serialize(new BrokerStatusPayload(
                    _brokerVersion,
                    MutationCapability.ReadOnly.IsEnabled,
                    DriverInstallEnabled: false,
                    BrokerOperationAllowlist.GetAllowedOperationNames()))),
            BrokerOperation.ValidatePlan => Success(
                request.RequestId,
                $"Plan '{request.PlanId!.Value}' hash accepted for validation."),
            _ => new BrokerResponse(
                request.RequestId,
                false,
                BrokerErrorCode.OperationNotAllowed,
                "Broker operation is not supported."),
        };
    }

    private static BrokerResponse Success(Guid requestId, string message, string? payloadJson = null) =>
        new(requestId, true, BrokerErrorCode.None, message, payloadJson);
}
