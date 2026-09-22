using System.Runtime.Versioning;
using Tortoise.Broker.Validation;
using Tortoise.Core.Mutation;
using Tortoise.Core.Recovery;

namespace Tortoise.Broker.Tests;

public sealed class BrokerInstallBoundaryGuardTests
{
    [Fact]
    public void ValidateEnvironment_blocks_when_pending_reboot_detector_reports_true()
    {
        var result = BrokerInstallBoundaryGuard.ValidateEnvironment(new FakePendingRebootDetector(true));

        Assert.False(result.IsAllowed);
        Assert.Contains("reboot", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateServicingLock_blocks_when_lock_not_acquired()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var servicingLock = MutationServicingLock.TryAcquire();
        using var competingLock = MutationServicingLock.TryAcquire();

        var result = BrokerInstallBoundaryGuard.ValidateServicingLock(competingLock);

        Assert.False(result.IsAllowed);
        Assert.Contains("servicing", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakePendingRebootDetector(bool pendingReboot) : IPendingRebootDetector
    {
        public bool IsPendingReboot() => pendingReboot;
    }
}
