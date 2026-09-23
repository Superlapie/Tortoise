using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Tortoise.Broker.Handling;
using Tortoise.Broker.Serialization;
using Tortoise.Broker.Validation;
using Tortoise.Contracts.Elevation;
using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Installation;
using Tortoise.Core.Mutation;
using Tortoise.Core.Planning;
using Tortoise.Persistence.Extensions;
using Tortoise.Persistence.Planning;
using Tortoise.Security.Broker;
using Tortoise.Windows.Devices;
using Tortoise.WindowsUpdate;
using Tortoise.WindowsUpdate.Environment;

namespace Tortoise.Broker.Ipc;

public sealed class BrokerPipeServer
{
    private static readonly TimeSpan AuthorizedClientListenDeadline = TimeSpan.FromSeconds(30);

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

        if (options.AuthorizedClientProcessId.HasValue && OperatingSystem.IsWindows())
        {
            await ServeWithClientBindingAsync(server, options, cancellationToken);
            return;
        }

        await server.WaitForConnectionAsync(cancellationToken);

        if (!BrokerPipeClientIdentity.IsAuthorizedClient(
                server,
                options.AuthorizedClientProcessId,
                out var clientIdentityError))
        {
            await WriteUnauthorizedResponseAsync(server, clientIdentityError, cancellationToken);
            return;
        }

        await ProcessAuthorizedConnectionAsync(server, options, cancellationToken);
    }

    private async Task ServeWithClientBindingAsync(
        NamedPipeServerStream server,
        BrokerHostOptions options,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.Add(AuthorizedClientListenDeadline);

        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            waitCts.CancelAfter(remaining);

            try
            {
                await server.WaitForConnectionAsync(waitCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!BrokerPipeClientIdentity.IsAuthorizedClient(
                    server,
                    options.AuthorizedClientProcessId,
                    out var clientIdentityError))
            {
                if (BrokerPipeClientIdentity.TryGetClientProcessId(server, out var rejectedProcessId))
                {
                    Trace.TraceWarning(
                        "Rejected pipe connection from PID {0}; expected {1}. {2}",
                        rejectedProcessId,
                        options.AuthorizedClientProcessId,
                        clientIdentityError ?? "Named pipe client is not authorized.");
                }

                server.Disconnect();
                continue;
            }

            await ProcessAuthorizedConnectionAsync(server, options, cancellationToken);
            return;
        }
    }

    private async Task ProcessAuthorizedConnectionAsync(
        PipeStream server,
        BrokerHostOptions options,
        CancellationToken cancellationToken)
    {
        var requestJson = await BrokerMessageSerializer.ReadMessageAsync(server, cancellationToken);
        var request = BrokerMessageSerializer.DeserializeRequest(requestJson);
        var response = await _handler.HandleAsync(request, options, cancellationToken);
        var responseJson = BrokerMessageSerializer.SerializeResponse(response);
        await BrokerMessageSerializer.WriteMessageAsync(server, responseJson, cancellationToken);
    }

    private static async Task WriteUnauthorizedResponseAsync(
        PipeStream server,
        string? clientIdentityError,
        CancellationToken cancellationToken)
    {
        var unauthorizedResponse = new BrokerResponse(
            Guid.Empty,
            false,
            BrokerErrorCode.ClientProcessMismatch,
            clientIdentityError ?? "Named pipe client is not authorized.");
        var unauthorizedJson = BrokerMessageSerializer.SerializeResponse(unauthorizedResponse);
        await BrokerMessageSerializer.WriteMessageAsync(server, unauthorizedJson, cancellationToken);
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

    public bool InstallOnlyMode { get; init; }

    public string? DatabasePath { get; init; }

    public int? AuthorizedClientProcessId { get; init; }

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

        return IsDisposableVmMutationEnabled();
    }

    public static bool ShouldAllowLabBrokerDriverInstall()
    {
        if (!MutationBuildPolicy.IsLabBrokerBuild)
        {
            return false;
        }

        return IsDisposableVmMutationEnabled();
    }

    private static bool IsDisposableVmMutationEnabled()
    {
        IExecutionEnvironmentDetector detector = OperatingSystem.IsWindows()
            ? new WindowsExecutionEnvironmentDetector()
            : new UnsupportedExecutionEnvironmentDetector();

        var capability = MutationCapabilityResolver.Resolve(detector.Detect());
        return capability.IsEnabled && capability.Environment == MutationEnvironment.DisposableVm;
    }
}

public static class BrokerHost
{
    public static Task RunAuthorizedInstallOnceAsync(BrokerHostOptions options, CancellationToken cancellationToken = default) =>
        RunOnceAsync(options, cancellationToken);

    public static Task RunOnceAsync(BrokerHostOptions options, CancellationToken cancellationToken = default)
    {
        IExecutionEnvironmentDetector detector = OperatingSystem.IsWindows()
            ? new WindowsExecutionEnvironmentDetector()
            : new UnsupportedExecutionEnvironmentDetector();

        IWindowsUpdateDriverInstallService? installService = options.AllowDriverInstall && OperatingSystem.IsWindows()
            ? new WindowsUpdateDriverInstallService()
            : null;

        var persistenceOptions = new Tortoise.Persistence.Options.TortoisePersistenceOptions();
        if (!string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            if (!BrokerDatabasePathValidator.TryValidate(options.DatabasePath, out var databasePath, out var validationError))
            {
                throw new InvalidOperationException(validationError ?? "Broker database path is invalid.");
            }

            if (!LabAuthoritativeStoreGuard.ValidateAuthoritativeStore(databasePath!, out var storeError))
            {
                throw new InvalidOperationException(storeError ?? "Lab authoritative store validation failed.");
            }

            persistenceOptions.DatabasePath = databasePath;
        }

        var planStore = new SqliteUpdatePlanStore(persistenceOptions);
        planStore.InitializeAsync(cancellationToken).GetAwaiter().GetResult();
        var planAuthority = new BrokerPlanAuthority(planStore);

        IDeviceInventoryProvider? deviceInventoryProvider = options.AllowDriverInstall && OperatingSystem.IsWindows()
            ? new WindowsDeviceInventoryProvider()
            : null;
        IBrokerLiveInstallVerifier? liveInstallVerifier = deviceInventoryProvider is not null
            ? new BrokerLiveInstallVerifier(
                deviceInventoryProvider,
                new WuaLiveWindowsUpdateCandidateProvider())
            : null;

        var handler = new BrokerRequestHandler(
            options,
            detector,
            planAuthority,
            installService,
            liveInstallVerifier);
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
            var brokerExe = Path.Combine(AppContext.BaseDirectory, "tortoise-lab-broker.exe");
            var brokerDll = Path.Combine(AppContext.BaseDirectory, "tortoise-lab-broker.dll");
            var legacyBrokerExe = Path.Combine(AppContext.BaseDirectory, "Tortoise.Broker.exe");
            var arguments =
                $"serve --session-id={options.SessionId} --pipe={options.GetEffectivePipeName()} --capability={options.CapabilityToken}";

            if (!string.IsNullOrWhiteSpace(options.DatabasePath)
                && BrokerDatabasePathValidator.TryValidate(options.DatabasePath, out var databasePath, out _))
            {
                arguments += $" --db=\"{databasePath}\"";
            }

            if (options.AuthorizedClientProcessId.HasValue)
            {
                arguments += $" --client-pid={options.AuthorizedClientProcessId.Value}";
            }

            System.Diagnostics.ProcessStartInfo startInfo;
            if (File.Exists(brokerExe))
            {
                startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = brokerExe,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                };
            }
            else if (File.Exists(brokerDll))
            {
                startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"\"{brokerDll}\" {arguments}",
                    UseShellExecute = true,
                    Verb = "runas",
                };
            }
            else if (File.Exists(legacyBrokerExe))
            {
                error = "tortoise-lab-broker was not found next to the lab host. Rebuild Tortoise.Lab to deploy the mutation broker.";
                return false;
            }
            else
            {
                error = "tortoise-lab-broker executable was not found next to the lab host.";
                return false;
            }

            return System.Diagnostics.Process.Start(startInfo) is not null;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
