using System.Runtime.Versioning;
using Tortoise.Core.Mutation;
using Tortoise.Core.Recovery;
using Tortoise.Windows.Recovery;

namespace Tortoise.Broker.Validation;

public sealed record BrokerInstallBoundaryResult(bool IsAllowed, string Message);

public static class BrokerInstallBoundaryGuard
{
    public static BrokerInstallBoundaryResult ValidateEnvironment(IPendingRebootDetector? pendingRebootDetector = null)
    {
        pendingRebootDetector ??= OperatingSystem.IsWindows()
            ? new WindowsPendingRebootDetector()
            : null;

        if (pendingRebootDetector?.IsPendingReboot() == true)
        {
            return Deny("Elevated broker install blocked because a system reboot is pending.");
        }

        return Allow();
    }

    public static BrokerInstallBoundaryResult ValidateServicingLock(MutationServicingLock? servicingLock)
    {
        if (servicingLock is null || !servicingLock.IsAcquired)
        {
            return Deny(
                "Elevated broker install blocked because another Tortoise servicing operation is in progress.");
        }

        return Allow();
    }

    private static BrokerInstallBoundaryResult Allow() =>
        new(true, "Broker install boundary checks passed.");

    private static BrokerInstallBoundaryResult Deny(string message) =>
        new(false, message);
}
