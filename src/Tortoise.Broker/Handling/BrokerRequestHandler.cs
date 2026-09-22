using System.Reflection;
using System.Text.Json;
using Tortoise.Contracts.Elevation;
using Tortoise.Contracts.Mutation;
using Tortoise.Core.Installation;
using Tortoise.Core.Mutation;
using Tortoise.Broker.Ipc;
using Tortoise.Security.Broker;

namespace Tortoise.Broker.Handling;

public sealed class BrokerRequestHandler
{
    private readonly BrokerHostOptions _options;
    private readonly IExecutionEnvironmentDetector _environmentDetector;
    private readonly IWindowsUpdateDriverInstallService? _installService;
    private readonly BrokerRequestValidator _validator = new();
    private readonly BrokerReplayGuard _replayGuard = new();
    private readonly string _brokerVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0-alpha";

    public BrokerRequestHandler(
        BrokerHostOptions options,
        IExecutionEnvironmentDetector environmentDetector,
        IWindowsUpdateDriverInstallService? installService = null)
    {
        _options = options;
        _environmentDetector = environmentDetector;
        _installService = installService;
    }

    public BrokerResponse Handle(BrokerRequest request, int expectedSessionId)
    {
        var validation = _validator.Validate(
            request,
            expectedSessionId,
            _replayGuard,
            _options.AllowDriverInstall);
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
            BrokerOperation.GetStatus => HandleGetStatus(request.RequestId),
            BrokerOperation.ValidatePlan => Success(
                request.RequestId,
                $"Plan '{request.PlanId!.Value}' hash accepted for validation."),
            BrokerOperation.InstallDriver => HandleInstallDriver(request),
            _ => new BrokerResponse(
                request.RequestId,
                false,
                BrokerErrorCode.OperationNotAllowed,
                "Broker operation is not supported."),
        };
    }

    private BrokerResponse HandleGetStatus(Guid requestId)
    {
        var capability = MutationCapabilityResolver.Resolve(_environmentDetector.Detect());
        var driverInstallEnabled = _options.AllowDriverInstall
            && capability.IsEnabled
            && capability.Environment == MutationEnvironment.DisposableVm;

        return Success(
            requestId,
            "Broker status available.",
            JsonSerializer.Serialize(new BrokerStatusPayload(
                _brokerVersion,
                capability.IsEnabled,
                driverInstallEnabled,
                BrokerOperationAllowlist.GetAllowedOperationNames(driverInstallEnabled))));
    }

    private BrokerResponse HandleInstallDriver(BrokerRequest request)
    {
        var capability = MutationCapabilityResolver.Resolve(_environmentDetector.Detect());
        if (!capability.IsEnabled || capability.Environment != MutationEnvironment.DisposableVm)
        {
            return new BrokerResponse(
                request.RequestId,
                false,
                BrokerErrorCode.MutationDisabled,
                "Driver installation requires disposable VM mutation capability.");
        }

        if (!_options.AllowDriverInstall || _installService is null)
        {
            return new BrokerResponse(
                request.RequestId,
                false,
                BrokerErrorCode.MutationDisabled,
                "Driver installation through the broker is not enabled.");
        }

        var payload = JsonSerializer.Deserialize<BrokerInstallPayload>(request.PayloadJson!);
        if (payload is null || string.IsNullOrWhiteSpace(payload.UpdateId))
        {
            return new BrokerResponse(
                request.RequestId,
                false,
                BrokerErrorCode.PlanValidationFailed,
                "Driver install payload is invalid.");
        }

        var installResult = _installService.InstallAsync(
            payload.UpdateId,
            payload.Revision).GetAwaiter().GetResult();

        if (!installResult.Succeeded)
        {
            return new BrokerResponse(
                request.RequestId,
                false,
                BrokerErrorCode.InternalError,
                installResult.Message,
                JsonSerializer.Serialize(installResult));
        }

        return Success(
            request.RequestId,
            installResult.Message,
            JsonSerializer.Serialize(installResult));
    }

    private static BrokerResponse Success(Guid requestId, string message, string? payloadJson = null) =>
        new(requestId, true, BrokerErrorCode.None, message, payloadJson);
}
