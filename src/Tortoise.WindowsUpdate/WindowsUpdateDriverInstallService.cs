using System.Runtime.Versioning;
using Tortoise.Core.Installation;

namespace Tortoise.WindowsUpdate;

[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateDriverInstallService : IWindowsUpdateDriverInstallService
{
    public Task<WindowsUpdateDownloadResult> DownloadAsync(
        string updateId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        return Task.FromResult(WuaInstallExecutor.Download(updateId, revision, cancellationToken));
    }

    public Task<WindowsUpdateInstallResult> InstallPreparedAsync(
        string updateId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        return Task.FromResult(WuaInstallExecutor.InstallPrepared(updateId, revision, cancellationToken));
    }

    public Task<WindowsUpdateInstallResult> InstallAsync(
        string updateId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        return Task.FromResult(WuaInstallExecutor.Install(updateId, revision, cancellationToken));
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(
                "WindowsUpdateDriverInstallService was invoked on a non-Windows operating system.");
        }
    }
}
