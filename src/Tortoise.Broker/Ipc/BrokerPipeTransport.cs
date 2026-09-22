using System.IO.Pipes;
using Tortoise.Broker.Handling;
using Tortoise.Broker.Serialization;
using Tortoise.Contracts.Elevation;
using Tortoise.Contracts.Mutation;
using Tortoise.Core.Installation;
using Tortoise.Core.Mutation;
using Tortoise.WindowsUpdate;
using Tortoise.WindowsUpdate.Environment;

namespace Tortoise.Broker.Ipc;

public sealed class BrokerPipeServer
{
    private readonly BrokerRequestHandler _handler;

    public BrokerPipeServer(BrokerRequestHandler handler) => _handler = handler;

    public async Task ServeOnceAsync(BrokerHostOptions options, CancellationToken cancellationToken = default)
    {
        var pipeName = options.PipeName ?? ElevationConstants.GetPipeName(options.SessionId);

        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await server.WaitForConnectionAsync(cancellationToken);

        var requestJson = await BrokerMessageSerializer.ReadMessageAsync(server, cancellationToken);
        var request = BrokerMessageSerializer.DeserializeRequest(requestJson);
        var response = _handler.Handle(request, options.SessionId);
        var responseJson = BrokerMessageSerializer.SerializeResponse(response);
        await BrokerMessageSerializer.WriteMessageAsync(server, responseJson, cancellationToken);
    }
}

public sealed class BrokerPipeClient
{
    public async Task<BrokerResponse> SendAsync(
        BrokerHostOptions options,
        BrokerRequest request,
        CancellationToken cancellationToken = default)
    {
        var pipeName = options.PipeName ?? ElevationConstants.GetPipeName(options.SessionId);

        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await client.ConnectAsync(options.ConnectTimeoutMs, cancellationToken);

        var requestJson = BrokerMessageSerializer.SerializeRequest(request);
        await BrokerMessageSerializer.WriteMessageAsync(client, requestJson, cancellationToken);

        var responseJson = await BrokerMessageSerializer.ReadMessageAsync(client, cancellationToken);
        return BrokerMessageSerializer.DeserializeResponse(responseJson);
    }
}

public sealed record BrokerHostOptions
{
    public required int SessionId { get; init; }

    public string? PipeName { get; init; }

    public int ConnectTimeoutMs { get; init; } = 5000;

    public bool AllowDriverInstall { get; init; }

    public static bool ShouldAllowDriverInstall()
    {
        IExecutionEnvironmentDetector detector = OperatingSystem.IsWindows()
            ? new WindowsExecutionEnvironmentDetector()
            : new UnsupportedExecutionEnvironmentDetector();

        var capability = MutationCapabilityResolver.Resolve(detector.Detect());
        return capability.IsEnabled && capability.Environment == MutationEnvironment.DisposableVm;
    }

    public static BrokerHostOptions ForSession(int sessionId, string? pipeName = null) =>
        new()
        {
            SessionId = sessionId,
            PipeName = pipeName,
            AllowDriverInstall = ShouldAllowDriverInstall(),
        };
}

public static class BrokerHost
{
    public static Task RunOnceAsync(BrokerHostOptions options, CancellationToken cancellationToken = default)
    {
        IExecutionEnvironmentDetector detector = OperatingSystem.IsWindows()
            ? new WindowsExecutionEnvironmentDetector()
            : new UnsupportedExecutionEnvironmentDetector();

        IWindowsUpdateDriverInstallService? installService = options.AllowDriverInstall && OperatingSystem.IsWindows()
            ? new WindowsUpdateDriverInstallService()
            : null;

        var handler = new BrokerRequestHandler(options, detector, installService);
        var server = new BrokerPipeServer(handler);
        return server.ServeOnceAsync(options, cancellationToken);
    }
}
