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

        return pendingRebootDetector?.DetectPendingReboot() switch
        {
            PendingRebootState.Pending => Deny(
                "Elevated broker install blocked because a system reboot is pending."),
            PendingRebootState.Unknown => Deny(
                "Elevated broker install blocked because pending-reboot status could not be determined."),
            _ => Allow(),
        };
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
