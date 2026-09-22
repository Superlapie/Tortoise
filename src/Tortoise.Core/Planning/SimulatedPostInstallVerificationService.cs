using Tortoise.Core.Devices;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Planning;

public sealed class SimulatedPostInstallVerificationService : ISimulatedPostInstallVerificationService
{
    public UpdateVerification VerifyPostInstall(
        StoredUpdatePlan storedPlan,
        DeviceInventoryEntry deviceBefore,
        DeviceInventoryEntry deviceAfter,
        Guid transactionId)
    {
        ArgumentNullException.ThrowIfNull(storedPlan);
        ArgumentNullException.ThrowIfNull(deviceBefore);
        ArgumentNullException.ThrowIfNull(deviceAfter);

        if (!deviceAfter.Snapshot.Identity.IsPresent)
        {
            return Failed(transactionId, "Post-install verification failed because the target device is no longer present.");
        }

        if (deviceAfter.Snapshot.Health.State == DeviceHealthState.Problem)
        {
            return Failed(
                transactionId,
                "Post-install verification failed because the target device reports a problem state.");
        }

        var beforeVersion = deviceBefore.InstalledDriver?.DriverVersion;
        var afterVersion = deviceAfter.InstalledDriver?.DriverVersion;
        if (beforeVersion is not null
            && afterVersion is not null
            && beforeVersion != afterVersion)
        {
            return Failed(
                transactionId,
                "Post-install verification failed because the installed driver version changed during simulation.");
        }

        return new UpdateVerification(
            transactionId,
            UpdateVerificationResult.Verified,
            "Simulated install completed; installed driver state remains unchanged as expected.",
            DateTimeOffset.UtcNow);
    }

    private static UpdateVerification Failed(Guid transactionId, string message) =>
        new(transactionId, UpdateVerificationResult.Failed, message, DateTimeOffset.UtcNow);
}
