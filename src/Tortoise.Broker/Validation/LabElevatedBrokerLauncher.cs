using System.Runtime.Versioning;
using Tortoise.Broker.Ipc;
using Tortoise.Core.Installation;
using Tortoise.Core.Planning;

namespace Tortoise.Broker.Validation;

[SupportedOSPlatform("windows")]
public sealed class LabElevatedBrokerLauncher : ILabElevatedBrokerLauncher
{
    public async Task LaunchAndWaitAsync(
        BrokerPlanValidationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("Elevated broker launch requires Windows.");
        }

        if (!BrokerDatabasePathValidator.TryValidate(options.DatabasePath, out var databasePath, out var validationError))
        {
            throw new InvalidOperationException(validationError ?? "Broker database path is invalid.");
        }

        LabAuthoritativeStoreGuard.EnsureProtectedStoreReady(databasePath!);

        var hostOptions = new BrokerHostOptions
        {
            SessionId = options.SessionId,
            CapabilityToken = options.CapabilityToken,
            PipeName = options.PipeName,
            ConnectTimeoutMs = options.ConnectTimeoutMs,
            AllowDriverInstall = true,
            InstallOnlyMode = true,
            DatabasePath = databasePath,
            AuthorizedClientProcessId = options.AuthorizedClientProcessId ?? Environment.ProcessId,
        };

        if (!BrokerElevationLauncher.TryLaunchElevatedBroker(hostOptions, out var launchError))
        {
            throw new InvalidOperationException(
                launchError ?? "Failed to launch the elevated Tortoise lab broker.");
        }

        await BrokerPipeAvailability.WaitUntilReadyAsync(
            hostOptions.GetEffectivePipeName(),
            TimeSpan.FromSeconds(30),
            cancellationToken);
    }
}
