using Tortoise.Contracts.Elevation;
using Tortoise.Core.Devices;
using Tortoise.Core.Planning;
using Tortoise.Core.Updates;
using Tortoise.Security.Broker;
using Tortoise.WindowsUpdate;

namespace Tortoise.Broker.Validation;

internal static class BrokerInstallToctouGuard
{
    internal static BrokerPlanAuthorityResult VerifyInstallBoundary(
        StoredUpdatePlan storedPlan,
        BrokerInstallPayload payload,
        DeviceInventoryEntry? liveDevice,
        WindowsUpdateCandidate? liveCandidate)
    {
        if (liveDevice is null)
        {
            return Deny("Target device is no longer present in the current inventory.");
        }

        if (!liveDevice.Snapshot.Identity.IsPresent)
        {
            return Deny("Target device is not present.");
        }

        var installedBinding = liveDevice.InstalledDriver;
        if (installedBinding?.DriverVersion is null
            || installedBinding.DriverVersion != storedPlan.Plan.CurrentDriver.Package.Identity.DriverVersion)
        {
            return Deny("Installed driver version changed since the plan was frozen.");
        }

        if (liveCandidate is null)
        {
            return Deny("Windows Update no longer returns the approved driver candidate.");
        }

        if (!string.Equals(liveCandidate.UpdateId, payload.UpdateId, StringComparison.OrdinalIgnoreCase)
            || liveCandidate.Revision != payload.Revision)
        {
            return Deny("Live Windows Update candidate identity does not match the install payload.");
        }

        if (liveCandidate.RequiresEula)
        {
            return Deny("Windows Update now requires EULA acceptance for this candidate.");
        }

        if (storedPlan.Classification != UpdateClassification.WindowsRecommended
            || liveCandidate.Classification != UpdateClassification.WindowsRecommended)
        {
            return Deny("Candidate classification is no longer Windows-recommended.");
        }

        if (string.IsNullOrWhiteSpace(liveCandidate.DriverHardwareId))
        {
            return Deny("Live candidate is missing authoritative driver hardware ID metadata.");
        }

        var identity = liveDevice.Snapshot.Identity;
        if (!WuaHardwareIdMatcher.MatchesDevice(liveCandidate, identity.HardwareIds, identity.CompatibleIds))
        {
            return Deny("Live candidate hardware ID no longer matches the target device.");
        }

        if (string.IsNullOrWhiteSpace(liveCandidate.DriverClass)
            && !liveCandidate.Categories.Any(category =>
                category.Contains("driver", StringComparison.OrdinalIgnoreCase)))
        {
            return Deny("Live candidate is no longer recognized as a driver update.");
        }

        return new BrokerPlanAuthorityResult(true, "Broker TOCTOU checks passed.", storedPlan);
    }

    private static BrokerPlanAuthorityResult Deny(string message) =>
        new(false, message);
}
