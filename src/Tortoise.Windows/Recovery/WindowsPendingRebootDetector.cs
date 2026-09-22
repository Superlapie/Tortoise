using System.Runtime.Versioning;
using Microsoft.Win32;
using Tortoise.Core.Recovery;

namespace Tortoise.Windows.Recovery;

[SupportedOSPlatform("windows")]
public sealed class WindowsPendingRebootDetector : IPendingRebootDetector
{
    public bool IsPendingReboot()
    {
        if (HasPendingFileRenameOperations())
        {
            return true;
        }

        if (HasWindowsUpdateRebootRequiredKey())
        {
            return true;
        }

        if (HasComponentBasedServicingRebootPending())
        {
            return true;
        }

        return false;
    }

    private static bool HasPendingFileRenameOperations()
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
            return false;
        }
    }

    private static bool HasWindowsUpdateRebootRequiredKey()
    {
        try
        {
            using var rebootRequiredKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            return rebootRequiredKey is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasComponentBasedServicingRebootPending()
    {
        try
        {
            using var rebootPendingKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            return rebootPendingKey is not null;
        }
        catch
        {
            return false;
        }
    }
}
