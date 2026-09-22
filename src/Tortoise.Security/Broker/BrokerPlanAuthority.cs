using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Tortoise.Contracts.Elevation;

namespace Tortoise.Security.Broker;

[SupportedOSPlatform("windows")]
public static class WindowsSessionIdentity
{
    public static int GetCurrentSessionId()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        return ProcessIdToSessionId((uint)Environment.ProcessId, out var sessionId)
            ? (int)sessionId
            : 0;
    }

    public static SecurityIdentifier? GetCurrentUserSid()
    {
        try
        {
            return WindowsIdentity.GetCurrent().User;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
}

public sealed record BrokerPlanAuthorityResult(
    bool IsAuthorized,
    string Message);

public interface IBrokerPlanAuthority
{
    Task<BrokerPlanAuthorityResult> ValidateAsync(
        Guid planId,
        string planHash,
        BrokerInstallPayload? installPayload,
        CancellationToken cancellationToken = default);
}
