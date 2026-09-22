using System.Reflection;
using System.Text.Json;
using Tortoise.Broker.Ipc;
using Tortoise.Broker.Validation;
using Tortoise.Contracts.Elevation;
using Tortoise.Contracts.Mutation;
using Tortoise.Core.Installation;
using Tortoise.Core.Mutation;
using Tortoise.Core.Updates;
using Tortoise.Security.Broker;

namespace Tortoise.Broker.Handling;

public sealed class BrokerRequestHandler
{
    private readonly BrokerHostOptions _options;
    private readonly IExecutionEnvironmentDetector _environmentDetector;
    private readonly IBrokerPlanAuthority _planAuthority;
    private readonly IWindowsUpdateDriverInstallService? _installService;
    private readonly IBrokerLiveInstallVerifier? _liveInstallVerifier;
    private readonly BrokerRequestValidator _validator;
    private readonly BrokerReplayGuard _replayGuard = new();
    private readonly string _brokerVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0-alpha";

    public BrokerRequestHandler(
        BrokerHostOptions options,
        IExecutionEnvironmentDetector environmentDetector,
        IBrokerPlanAuthority planAuthority,
        IWindowsUpdateDriverInstallService? installService = null,
        IBrokerLiveInstallVerifier? liveInstallVerifier = null)
    {
        _options = options;
        _environmentDetector = environmentDetector;
        _planAuthority = planAuthority;
        _installService = installService;
        _liveInstallVerifier = liveInstallVerifier;
        _validator = new BrokerRequestValidator(options.CapabilityToken);
    }

    public async Task<BrokerResponse> HandleAsync(
        BrokerRequest request,
        BrokerHostOptions options,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(
            request,
            options.SessionId,
            _replayGuard,
            options.AllowDriverInstall);
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
            BrokerOperation.ValidatePlan => await HandleValidatePlanAsync(request, cancellationToken),
            BrokerOperation.InstallDriver => await HandleInstallDriverAsync(request, cancellationToken),
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
                BrokerOperationAllowlist.GetAllowedOperationNames(driverInstallEnabled),
                _options.SessionId)));
    }

    private async Task<BrokerResponse> HandleValidatePlanAsync(
        BrokerRequest request,
        CancellationToken cancellationToken)
    {
        var authority = await _planAuthority.ValidateAsync(
            request.PlanId!.Value,
            request.PlanHash!,
            installPayload: null,
            cancellationToken);

        if (!authority.IsAuthorized)
        {
            return new BrokerResponse(
                request.RequestId,
                false,
                BrokerErrorCode.PlanValidationFailed,
                authority.Message);
        }

        return Success(request.RequestId, authority.Message);
    }

    private async Task<BrokerResponse> HandleInstallDriverAsync(
        BrokerRequest request,
        CancellationToken cancellationToken)
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

        var authority = await _planAuthority.ValidateAsync(
            request.PlanId!.Value,
            request.PlanHash!,
            payload,
            cancellationToken);

        if (!authority.IsAuthorized || authority.StoredPlan is null)
        {
            return new BrokerResponse(
                request.RequestId,
                false,
                BrokerErrorCode.PlanValidationFailed,
                authority.Message);
        }

        if (_liveInstallVerifier is not null)
        {
            var toctou = await _liveInstallVerifier.VerifyInstallBoundaryAsync(
                authority.StoredPlan,
                payload,
                cancellationToken);

            if (!toctou.IsAuthorized)
            {
                return new BrokerResponse(
                    request.RequestId,
                    false,
                    BrokerErrorCode.PlanValidationFailed,
                    toctou.Message);
            }
        }

        var installResult = await _installService.InstallAsync(
            payload.UpdateId,
            payload.Revision);

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
