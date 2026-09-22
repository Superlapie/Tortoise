using System.Runtime.Versioning;
using Tortoise.Core.Installation;

namespace Tortoise.WindowsUpdate;

[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateDriverInstallService : IWindowsUpdateDriverInstallService
{
    public Task<WindowsUpdateInstallResult> InstallAsync(
        string updateId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(
                "WindowsUpdateDriverInstallService was invoked on a non-Windows operating system.");
        }

        var result = WuaInstallExecutor.Install(updateId, revision, cancellationToken);
        return Task.FromResult(result);
    }
}
