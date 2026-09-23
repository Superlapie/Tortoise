using System.Diagnostics;
using Tortoise.Broker.Ipc;
using Tortoise.Broker.Validation;
using Tortoise.Contracts.Elevation;
using Tortoise.Contracts.Mutation;
using Tortoise.Core.Mutation;
using Tortoise.Core.Recovery;
using Tortoise.Security.Broker;

namespace Tortoise.Broker.Tests;

public sealed class BrokerInstallBoundaryGuardTests
{
    [Fact]
    public void ValidateEnvironment_blocks_when_pending_reboot_detector_reports_pending()
    {
        var result = BrokerInstallBoundaryGuard.ValidateEnvironment(
            new FakePendingRebootDetector(PendingRebootState.Pending));

        Assert.False(result.IsAllowed);
        Assert.Contains("reboot is pending", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateEnvironment_blocks_when_pending_reboot_detector_reports_unknown()
    {
        var result = BrokerInstallBoundaryGuard.ValidateEnvironment(
            new FakePendingRebootDetector(PendingRebootState.Unknown));

        Assert.False(result.IsAllowed);
        Assert.Contains("could not be determined", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ValidateServicingLock_blocks_when_competing_process_holds_lock()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var probePath = LocateMutationLockProbe();
        if (probePath is null)
        {
            throw new InvalidOperationException("tortoise-mutation-lock-probe was not found.");
        }

        Process? servicingHolder = null;
        try
        {
            servicingHolder = ProcessProbeTestSupport.StartProbe(probePath, "hold", "servicing");
            await ProcessProbeTestSupport.WaitForOutputLineAsync(
                servicingHolder,
                "HOLDING",
                ProcessProbeTestSupport.DefaultProbeTimeout);

            using var competingLock = MutationServicingLock.TryAcquire();
            var result = BrokerInstallBoundaryGuard.ValidateServicingLock(competingLock);

            Assert.False(result.IsAllowed);
            Assert.Contains("servicing", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ProcessProbeTestSupport.EnsureTerminated(servicingHolder);
        }
    }

    private static string? LocateMutationLockProbe()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tortoise-mutation-lock-probe.dll"),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "Tortoise.MutationLockProbe",
                "bin",
                "Debug",
                "net10.0",
                "tortoise-mutation-lock-probe.dll")),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private sealed class FakePendingRebootDetector(PendingRebootState state) : IPendingRebootDetector
    {
        public PendingRebootState DetectPendingReboot() => state;
    }
}
