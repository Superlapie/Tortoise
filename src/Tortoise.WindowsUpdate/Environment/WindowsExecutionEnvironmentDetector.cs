using System.Runtime.Versioning;
using Microsoft.Win32;
using Tortoise.Contracts.Mutation;
using Tortoise.Core.Mutation;

namespace Tortoise.WindowsUpdate.Environment;

[SupportedOSPlatform("windows")]
public sealed class WindowsExecutionEnvironmentDetector : IExecutionEnvironmentDetector
{
    public ExecutionEnvironmentInfo Detect()
    {
        var mutationTests = TortoiseEnvironmentVariables.IsTruthy(
            global::System.Environment.GetEnvironmentVariable(TortoiseEnvironmentVariables.MutationTests));
        var allowVmInstall = TortoiseEnvironmentVariables.IsTruthy(
            global::System.Environment.GetEnvironmentVariable(TortoiseEnvironmentVariables.AllowVmInstall));
        var physicalPilot = TortoiseEnvironmentVariables.IsTruthy(
            global::System.Environment.GetEnvironmentVariable(TortoiseEnvironmentVariables.PhysicalPilot));

        return new ExecutionEnvironmentInfo(
            IsDisposableVm: DetectGuestVm(),
            mutationTests,
            allowVmInstall,
            physicalPilot);
    }

    private static bool DetectGuestVm()
    {
        try
        {
            using var guestKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters");
            if (guestKey is not null)
            {
                return true;
            }

            using var hypervisorKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Hyper-V\Guest\Parameters");
            return hypervisorKey is not null;
        }
        catch
        {
            return false;
        }
    }
}
