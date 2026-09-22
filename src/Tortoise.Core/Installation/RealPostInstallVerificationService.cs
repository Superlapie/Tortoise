using Tortoise.Core.Devices;
using Tortoise.Core.Planning;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Installation;

public sealed class RealPostInstallVerificationService : IRealPostInstallVerificationService
{
    public UpdateVerification VerifyPostInstall(
        StoredUpdatePlan storedPlan,
        DeviceInventoryEntry deviceBefore,
        DeviceInventoryEntry deviceAfter,
        Guid operationId)
    {
        ArgumentNullException.ThrowIfNull(storedPlan);
        ArgumentNullException.ThrowIfNull(deviceBefore);
        ArgumentNullException.ThrowIfNull(deviceAfter);

        if (!deviceAfter.Snapshot.Identity.IsPresent)
        {
            return Failed(operationId, "Post-install verification failed because the target device is no longer present.");
        }

        if (deviceAfter.Snapshot.Health.State == DeviceHealthState.Problem)
        {
            return Failed(
                operationId,
                "Post-install verification failed because the target device reports a problem state.");
        }

        var proposedVersion = storedPlan.Plan.ProposedUpdate.Candidate.Package.Identity.DriverVersion;
        var afterVersion = deviceAfter.InstalledDriver?.DriverVersion;
        if (afterVersion is null)
        {
            return Failed(
                operationId,
                "Post-install verification failed because installed driver metadata is unavailable.");
        }

        if (afterVersion != proposedVersion)
        {
            return Failed(
                operationId,
                $"Post-install verification failed because installed driver version '{afterVersion}' does not match proposed version '{proposedVersion}'.");
        }

        return new UpdateVerification(
            operationId,
            UpdateVerificationResult.Verified,
            "Installed driver version matches the frozen plan.",
            DateTimeOffset.UtcNow);
    }

    private static UpdateVerification Failed(Guid operationId, string message) =>
        new(operationId, UpdateVerificationResult.Failed, message, DateTimeOffset.UtcNow);
}
