using System.Security.Cryptography;
using System.Text;
using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Updates;

public static class UpdatePlanFactory
{
    public static UpdatePlan Create(
        DeviceSnapshot deviceSnapshot,
        InstalledDriver currentDriver,
        DriverUpdate proposedUpdate,
        string safetyPolicyVersion,
        string applicationVersion,
        bool restartExpected,
        UpdateClassification classification,
        DriverRiskLevel riskLevel)
    {
        var planId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var hash = ComputeHash(
            planId,
            deviceSnapshot,
            currentDriver,
            proposedUpdate,
            safetyPolicyVersion,
            classification,
            riskLevel);

        return new UpdatePlan(
            planId,
            createdAt,
            deviceSnapshot,
            currentDriver,
            proposedUpdate,
            safetyPolicyVersion,
            applicationVersion,
            restartExpected,
            hash);
    }

    public static string ComputeHash(
        Guid planId,
        DeviceSnapshot deviceSnapshot,
        InstalledDriver currentDriver,
        DriverUpdate proposedUpdate,
        string safetyPolicyVersion,
        UpdateClassification classification,
        DriverRiskLevel riskLevel)
    {
        var candidate = proposedUpdate.Candidate;
        var payload = string.Join('|',
            planId,
            deviceSnapshot.Identity.DeviceInstanceId,
            string.Join(',', deviceSnapshot.Identity.HardwareIds),
            deviceSnapshot.Identity.ClassGuid,
            currentDriver.Package.Identity.ProviderName,
            currentDriver.Package.Identity.PublishedInfName,
            currentDriver.Package.Identity.DriverVersion,
            candidate.UpdateIdentity,
            candidate.UpdateRevision.ToString(),
            proposedUpdate.Classification,
            proposedUpdate.RequiresEula,
            proposedUpdate.RestartRequired,
            classification,
            riskLevel,
            safetyPolicyVersion);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }
}

public static class StalePlanDetector
{
    public static bool IsStale(
        UpdatePlan plan,
        DeviceSnapshot currentDevice,
        InstalledDriver currentDriver,
        UpdateClassification classification,
        DriverRiskLevel riskLevel)
    {
        if (!string.Equals(
                plan.DeviceSnapshot.Identity.DeviceInstanceId,
                currentDevice.Identity.DeviceInstanceId,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (plan.CurrentDriver.Package.Identity.DriverVersion != currentDriver.Package.Identity.DriverVersion)
        {
            return true;
        }

        var expectedHash = UpdatePlanFactory.ComputeHash(
            plan.PlanId,
            plan.DeviceSnapshot,
            plan.CurrentDriver,
            plan.ProposedUpdate,
            plan.SafetyPolicyVersion,
            classification,
            riskLevel);

        return !string.Equals(plan.PlanHash, expectedHash, StringComparison.Ordinal);
    }
}
