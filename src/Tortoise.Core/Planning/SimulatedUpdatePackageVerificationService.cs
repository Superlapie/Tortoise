using Tortoise.Core.Drivers;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Planning;

public sealed class SimulatedUpdatePackageVerificationService : ISimulatedUpdatePackageVerificationService
{
    public UpdateVerification VerifyPackage(StoredUpdatePlan storedPlan, Guid transactionId)
    {
        ArgumentNullException.ThrowIfNull(storedPlan);

        var expectedHash = UpdatePlanFactory.ComputeHash(
            storedPlan.Plan.PlanId,
            storedPlan.Plan.DeviceSnapshot,
            storedPlan.Plan.CurrentDriver,
            storedPlan.Plan.ProposedUpdate,
            storedPlan.Plan.SafetyPolicyVersion);

        if (!string.Equals(storedPlan.Plan.PlanHash, expectedHash, StringComparison.Ordinal))
        {
            return Failed(
                transactionId,
                "Package verification failed because the frozen plan hash does not match its contents.");
        }

        var package = storedPlan.Plan.ProposedUpdate.Candidate.Package;
        if (!package.Signature.IsSigned)
        {
            return Failed(
                transactionId,
                "Package verification failed because the proposed package is not signed.");
        }

        var currentVersion = storedPlan.Plan.CurrentDriver.Package.Identity.DriverVersion;
        var proposedVersion = package.Identity.DriverVersion;
        if (proposedVersion <= currentVersion)
        {
            return Failed(
                transactionId,
                "Package verification failed because the proposed driver version is not newer than the installed driver.");
        }

        return new UpdateVerification(
            transactionId,
            UpdateVerificationResult.Verified,
            "Simulated package metadata matches the frozen plan.",
            DateTimeOffset.UtcNow);
    }

    private static UpdateVerification Failed(Guid transactionId, string message) =>
        new(transactionId, UpdateVerificationResult.Failed, message, DateTimeOffset.UtcNow);
}
