using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Tortoise.Broker.Handling;
using Tortoise.Broker.Serialization;
using Tortoise.Contracts.Elevation;
using Tortoise.Contracts.Mutation;
using Tortoise.Core.Installation;
using Tortoise.Core.Mutation;
using Tortoise.Core.Planning;
using Tortoise.Persistence.Extensions;
using Tortoise.Persistence.Planning;
using Tortoise.Security.Broker;
using Tortoise.WindowsUpdate;
using Tortoise.WindowsUpdate.Environment;

namespace Tortoise.Broker.Ipc;

public sealed class BrokerPipeServer
{
    private readonly BrokerRequestHandler _handler;

    public BrokerPipeServer(BrokerRequestHandler handler) => _handler = handler;

    public async Task ServeOnceAsync(BrokerHostOptions options, CancellationToken cancellationToken = default)
    {
        var pipeName = options.GetEffectivePipeName();

        await using var server = OperatingSystem.IsWindows()
            ? CreateRestrictedServer(pipeName)
            : new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

        await server.WaitForConnectionAsync(cancellationToken);

        var requestJson = await BrokerMessageSerializer.ReadMessageAsync(server, cancellationToken);
        var request = BrokerMessageSerializer.DeserializeRequest(requestJson);
        var response = await _handler.HandleAsync(request, options, cancellationToken);
        var responseJson = BrokerMessageSerializer.SerializeResponse(response);
        await BrokerMessageSerializer.WriteMessageAsync(server, responseJson, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreateRestrictedServer(string pipeName)
    {
        var pipeSecurity = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser is not null)
        {
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                currentUser,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
        }

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity);
    }
}

public sealed class BrokerPipeClient
{
    public async Task<BrokerResponse> SendAsync(
        BrokerHostOptions options,
        BrokerRequest request,
        CancellationToken cancellationToken = default)
    {
        var pipeName = options.GetEffectivePipeName();

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

    public required string CapabilityToken { get; init; }

    public string? PipeName { get; init; }

    public int ConnectTimeoutMs { get; init; } = 5000;

    public bool AllowDriverInstall { get; init; }

    public string GetEffectivePipeName() =>
        PipeName ?? ElevationConstants.GetPipeName(SessionId, CapabilityToken);

    public static BrokerHostOptions Create(bool allowDriverInstall = false)
    {
        var sessionId = OperatingSystem.IsWindows()
            ? WindowsSessionIdentity.GetCurrentSessionId()
            : Environment.ProcessId;
        return new BrokerHostOptions
        {
            SessionId = sessionId,
            CapabilityToken = Guid.NewGuid().ToString("N"),
            AllowDriverInstall = allowDriverInstall && MutationBuildPolicy.AllowsRealMutation,
        };
    }

    public static bool ShouldAllowDriverInstall()
    {
        if (!MutationBuildPolicy.AllowsRealMutation)
        {
            return false;
        }

        IExecutionEnvironmentDetector detector = OperatingSystem.IsWindows()
            ? new WindowsExecutionEnvironmentDetector()
            : new UnsupportedExecutionEnvironmentDetector();

        var capability = MutationCapabilityResolver.Resolve(detector.Detect());
        return capability.IsEnabled && capability.Environment == MutationEnvironment.DisposableVm;
    }
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

        var planStore = new SqliteUpdatePlanStore(new Tortoise.Persistence.Options.TortoisePersistenceOptions());
        planStore.InitializeAsync(cancellationToken).GetAwaiter().GetResult();
        var planAuthority = new BrokerPlanAuthority(planStore);

        var handler = new BrokerRequestHandler(options, detector, planAuthority, installService);
        var server = new BrokerPipeServer(handler);
        return server.ServeOnceAsync(options, cancellationToken);
    }
}

public static class BrokerElevationLauncher
{
    [SupportedOSPlatform("windows")]
    public static bool TryLaunchElevatedBroker(BrokerHostOptions options, out string? error)
    {
        error = null;
        try
        {
            var brokerPath = Path.Combine(AppContext.BaseDirectory, "Tortoise.Broker.exe");
            if (!File.Exists(brokerPath))
            {
                brokerPath = Path.Combine(AppContext.BaseDirectory, "Tortoise.Broker.dll");
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = brokerPath,
                Arguments =
                    $"serve --session-id={options.SessionId} --pipe={options.GetEffectivePipeName()} --capability={options.CapabilityToken}",
                UseShellExecute = true,
                Verb = "runas",
            };

            return System.Diagnostics.Process.Start(startInfo) is not null;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
