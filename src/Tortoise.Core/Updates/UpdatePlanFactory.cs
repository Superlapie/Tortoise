using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Updates;

public static class UpdatePlanFactory
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

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
        var payload = new UpdatePlanCanonicalPayload(
            planId,
            deviceSnapshot.Identity.DeviceInstanceId,
            deviceSnapshot.Identity.HardwareIds.ToArray(),
            deviceSnapshot.Identity.CompatibleIds.ToArray(),
            deviceSnapshot.Identity.ClassGuid,
            currentDriver.Package.Identity.ProviderName,
            currentDriver.Package.Identity.PublishedInfName,
            currentDriver.Package.Identity.DriverVersion.ToString(),
            candidate.UpdateIdentity,
            candidate.UpdateRevision,
            proposedUpdate.Classification.ToString(),
            proposedUpdate.RequiresEula,
            proposedUpdate.RestartRequired,
            classification.ToString(),
            riskLevel.ToString(),
            safetyPolicyVersion);

        var json = JsonSerializer.Serialize(payload, CanonicalJsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash);
    }

    private sealed record UpdatePlanCanonicalPayload(
        Guid PlanId,
        string DeviceInstanceId,
        string[] HardwareIds,
        string[] CompatibleIds,
        Guid ClassGuid,
        string CurrentProviderName,
        string CurrentPublishedInfName,
        string CurrentDriverVersion,
        string CandidateUpdateId,
        int CandidateUpdateRevision,
        string ProposedClassification,
        bool RequiresEula,
        bool RestartRequired,
        string StoredClassification,
        string RiskLevel,
        string SafetyPolicyVersion);
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
