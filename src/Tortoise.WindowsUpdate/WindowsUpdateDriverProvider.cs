using System.Runtime.Versioning;
using Tortoise.Core.Errors;
using Tortoise.Core.Updates;

namespace Tortoise.WindowsUpdate;

[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateDriverProvider : IDriverUpdateProvider
{
    public Task<DriverUpdateScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new TortoiseException(
                TortoiseErrorCategory.WindowsUpdateError,
                "Windows Update scanning is only supported on Windows.",
                "WindowsUpdateDriverProvider was invoked on a non-Windows operating system.");
        }

        return Task.Run(() => WuaSearchExecutor.Search(cancellationToken), cancellationToken);
    }

    public Task<WindowsUpdateCandidateDetails?> GetDetailsAsync(
        string updateId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updateId);

        if (!OperatingSystem.IsWindows())
        {
            throw new TortoiseException(
                TortoiseErrorCategory.WindowsUpdateError,
                "Windows Update scanning is only supported on Windows.",
                "WindowsUpdateDriverProvider was invoked on a non-Windows operating system.");
        }

        return Task.Run(
            () => WuaSearchExecutor.GetDetails(updateId, revision, cancellationToken),
            cancellationToken);
    }
}
