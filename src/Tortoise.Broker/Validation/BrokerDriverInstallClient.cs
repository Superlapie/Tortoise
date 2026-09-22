using System.Text.Json;
using Tortoise.Contracts.Elevation;
using Tortoise.Core.Installation;
using Tortoise.Core.Planning;
using Tortoise.Broker.Ipc;
using Tortoise.Security.Broker;

namespace Tortoise.Broker.Validation;

public sealed class BrokerDriverInstallClient : IBrokerDriverInstallClient
{
    private readonly BrokerPipeClient _client = new();

    public async Task<BrokerDriverInstallResult> InstallDriverAsync(
        Guid planId,
        string planHash,
        string updateId,
        int revision,
        BrokerPlanValidationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var hostOptions = new BrokerHostOptions
        {
            SessionId = options.SessionId,
            PipeName = options.PipeName,
            ConnectTimeoutMs = options.ConnectTimeoutMs,
            AllowDriverInstall = BrokerHostOptions.ShouldAllowDriverInstall(),
        };

        var response = await _client.SendAsync(
            hostOptions,
            BrokerRequestFactory.CreateInstallDriver(
                options.SessionId,
                planId,
                planHash,
                updateId,
                revision),
            cancellationToken);

        if (!response.Succeeded)
        {
            return new BrokerDriverInstallResult(
                false,
                response.Message,
                ResultCode: 4,
                RebootRequired: false,
                response.ErrorCode.ToString());
        }

        if (string.IsNullOrWhiteSpace(response.PayloadJson))
        {
            return new BrokerDriverInstallResult(
                true,
                response.Message,
                ResultCode: 2,
                RebootRequired: false);
        }

        var installResult = JsonSerializer.Deserialize<WindowsUpdateInstallResult>(response.PayloadJson);
        if (installResult is null)
        {
            return new BrokerDriverInstallResult(
                true,
                response.Message,
                ResultCode: 2,
                RebootRequired: false);
        }

        return new BrokerDriverInstallResult(
            installResult.Succeeded,
            installResult.Message,
            installResult.ResultCode,
            installResult.RebootRequired);
    }
}
