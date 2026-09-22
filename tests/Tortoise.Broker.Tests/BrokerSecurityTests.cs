using Tortoise.Broker.Handling;
using Tortoise.Broker.Ipc;
using Tortoise.Contracts.Elevation;
using Tortoise.Security.Broker;

namespace Tortoise.Broker.Tests;

public sealed class BrokerRequestValidatorTests
{
    private readonly BrokerRequestValidator _validator = new();

    [Fact]
    public void Validate_rejects_replayed_nonce()
    {
        var replayGuard = new BrokerReplayGuard();
        var request = BrokerRequestFactory.Create(BrokerOperation.Ping, sessionId: 42);

        Assert.True(_validator.Validate(request, 42, replayGuard).IsValid);
        Assert.False(_validator.Validate(request, 42, replayGuard).IsValid);
    }

    [Fact]
    public void Validate_rejects_session_mismatch()
    {
        var replayGuard = new BrokerReplayGuard();
        var request = BrokerRequestFactory.Create(BrokerOperation.Ping, sessionId: 42);

        var result = _validator.Validate(request, expectedSessionId: 99, replayGuard);

        Assert.False(result.IsValid);
        Assert.Equal(BrokerErrorCode.SessionMismatch, result.ErrorCode);
    }

    [Fact]
    public void Validate_rejects_install_driver_operation()
    {
        var replayGuard = new BrokerReplayGuard();
        var request = BrokerRequestFactory.Create(BrokerOperation.InstallDriver, sessionId: 42);

        var result = _validator.Validate(request, 42, replayGuard);

        Assert.False(result.IsValid);
        Assert.Equal(BrokerErrorCode.MutationDisabled, result.ErrorCode);
    }

    [Fact]
    public void Validate_requires_plan_hash_for_validate_plan()
    {
        var replayGuard = new BrokerReplayGuard();
        var request = BrokerRequestFactory.Create(
            BrokerOperation.ValidatePlan,
            sessionId: 42,
            planId: Guid.NewGuid(),
            planHash: null);

        var result = _validator.Validate(request, 42, replayGuard);

        Assert.False(result.IsValid);
        Assert.Equal(BrokerErrorCode.PlanValidationFailed, result.ErrorCode);
    }
}

public sealed class BrokerRequestHandlerTests
{
    private readonly BrokerRequestHandler _handler = new();

    [Fact]
    public void Handle_returns_status_with_install_disabled()
    {
        var request = BrokerRequestFactory.Create(BrokerOperation.GetStatus, sessionId: 7);
        var response = _handler.Handle(request, expectedSessionId: 7);

        Assert.True(response.Succeeded);
        Assert.Contains("\"driverInstallEnabled\":false", response.PayloadJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ValidatePlan", response.PayloadJson!, StringComparison.Ordinal);
    }
}

public sealed class BrokerPipeIntegrationTests
{
    [Fact]
    public async Task Client_and_server_exchange_ping_over_named_pipe()
    {
        var sessionId = Random.Shared.Next(1000, 9999);
        var pipeName = $"Tortoise.Broker.Test.{Guid.NewGuid():N}";
        var options = new BrokerHostOptions
        {
            SessionId = sessionId,
            PipeName = pipeName,
            ConnectTimeoutMs = 5000,
        };

        var serverTask = BrokerHost.RunOnceAsync(options);
        await Task.Delay(100);

        var client = new BrokerPipeClient();
        var response = await client.SendAsync(options, BrokerRequestFactory.Create(BrokerOperation.Ping, sessionId));

        Assert.True(response.Succeeded);
        await serverTask;
    }

    [Fact]
    public async Task Client_and_server_validate_plan_over_named_pipe()
    {
        var sessionId = Random.Shared.Next(1000, 9999);
        var pipeName = $"Tortoise.Broker.Test.{Guid.NewGuid():N}";
        var options = new BrokerHostOptions
        {
            SessionId = sessionId,
            PipeName = pipeName,
            ConnectTimeoutMs = 5000,
        };

        var serverTask = BrokerHost.RunOnceAsync(options);
        await Task.Delay(100);

        var client = new BrokerPipeClient();
        var response = await client.SendAsync(
            options,
            BrokerRequestFactory.Create(
                BrokerOperation.ValidatePlan,
                sessionId,
                planId: Guid.NewGuid(),
                planHash: new string('A', 64)));

        Assert.True(response.Succeeded);
        await serverTask;
    }
}
