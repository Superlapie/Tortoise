using System.Runtime.Versioning;
using Microsoft.Win32;
using Tortoise.Core.Recovery;

namespace Tortoise.Windows.Recovery;

[SupportedOSPlatform("windows")]
public sealed class WindowsPendingRebootDetector : IPendingRebootDetector
{
    public PendingRebootState DetectPendingReboot()
    {
        var states = new[]
        {
            EvaluateIndicator(HasPendingFileRenameOperations),
            EvaluateIndicator(HasWindowsUpdateRebootRequiredKey),
            EvaluateIndicator(HasComponentBasedServicingRebootPending),
        };

        if (states.Any(state => state == PendingRebootState.Pending))
        {
            return PendingRebootState.Pending;
        }

        if (states.Any(state => state == PendingRebootState.Unknown))
        {
            return PendingRebootState.Unknown;
        }

        return PendingRebootState.NotPending;
    }

    private static PendingRebootState EvaluateIndicator(Func<bool?> probe)
    {
        return probe() switch
        {
            true => PendingRebootState.Pending,
            false => PendingRebootState.NotPending,
            null => PendingRebootState.Unknown,
        };
    }

    private static bool? HasPendingFileRenameOperations()
    {
        try
        {
            using var sessionManager = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager");
            var pendingRenames = sessionManager?.GetValue("PendingFileRenameOperations") as string[];
            return pendingRenames is { Length: > 0 };
        }
        catch
        {
            return null;
        }
    }

    private static bool? HasWindowsUpdateRebootRequiredKey()
    {
        try
        {
            using var rebootRequiredKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            return rebootRequiredKey is not null;
        }
        catch
        {
            return null;
        }
    }

    private static bool? HasComponentBasedServicingRebootPending()
    {
        try
        {
            using var rebootPendingKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            return rebootPendingKey is not null;
        }
        catch
        {
            return null;
        }
    }
}
