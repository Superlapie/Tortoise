using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
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

        if (deviceBefore.Snapshot.Health.ProblemCode is null
            && deviceAfter.Snapshot.Health.ProblemCode is not null)
        {
            return Failed(
                operationId,
                "Post-install verification failed because the target device acquired a new problem code.");
        }

        var afterBinding = deviceAfter.InstalledDriver;
        if (afterBinding is null)
        {
            return Failed(
                operationId,
                "Post-install verification failed because installed driver metadata is unavailable.");
        }

        var candidate = storedPlan.Plan.ProposedUpdate.Candidate;
        var proposedVersion = candidate.Package.Identity.DriverVersion;
        if (DriverVersionEvidence.IsKnown(proposedVersion))
        {
            if (afterBinding.DriverVersion != proposedVersion)
            {
                return Failed(
                    operationId,
                    $"Post-install verification failed because installed driver version '{afterBinding.DriverVersion}' does not match the known proposed version '{proposedVersion}'.");
            }

            return Verified(operationId, "Installed driver version matches the frozen plan.");
        }

        var beforeBinding = deviceBefore.InstalledDriver;
        if (!HasObservableDriverChange(beforeBinding, afterBinding))
        {
            return Inconclusive(
                operationId,
                "Post-install verification is inconclusive because no observable installed-driver change was detected.");
        }

        var expectedProvider = candidate.Package.Identity.ProviderName;
        if (!string.IsNullOrWhiteSpace(expectedProvider)
            && !string.Equals(afterBinding.ProviderName, expectedProvider, StringComparison.OrdinalIgnoreCase))
        {
            return Failed(
                operationId,
                $"Post-install verification failed because installed provider '{afterBinding.ProviderName}' does not match expected provider '{expectedProvider}'.");
        }

        var expectedDriverDate = candidate.Package.Identity.DriverDate;
        if (expectedDriverDate is not null
            && afterBinding.DriverDate is not null
            && afterBinding.DriverDate != expectedDriverDate)
        {
            return Inconclusive(
                operationId,
                "Post-install verification is inconclusive because installed driver date does not match the planned candidate date.");
        }

        return Verified(
            operationId,
            "Post-install verification passed using installed-driver evidence (expected driver version remains unknown).");
    }

    private static bool HasObservableDriverChange(
        DeviceDriverBinding? beforeBinding,
        DeviceDriverBinding afterBinding)
    {
        if (beforeBinding is null)
        {
            return true;
        }

        if (beforeBinding.DriverVersion != afterBinding.DriverVersion
            && afterBinding.DriverVersion is not null)
        {
            return true;
        }

        if (!string.Equals(beforeBinding.InfName, afterBinding.InfName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(beforeBinding.ProviderName, afterBinding.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return beforeBinding.DriverDate != afterBinding.DriverDate;
    }

    private static UpdateVerification Verified(Guid operationId, string message) =>
        new(operationId, UpdateVerificationResult.Verified, message, DateTimeOffset.UtcNow);

    private static UpdateVerification Inconclusive(Guid operationId, string message) =>
        new(operationId, UpdateVerificationResult.InstalledInconclusive, message, DateTimeOffset.UtcNow);

    private static UpdateVerification Failed(Guid operationId, string message) =>
        new(operationId, UpdateVerificationResult.Failed, message, DateTimeOffset.UtcNow);
}
