using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Tortoise.Core.Recovery;
using Tortoise.Core.Updates;

namespace Tortoise.Windows.Recovery;

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemEnvironmentProvider : ISystemEnvironmentProvider
{
    private readonly IPendingRebootDetector _pendingRebootDetector;

    public WindowsSystemEnvironmentProvider(IPendingRebootDetector? pendingRebootDetector = null)
    {
        _pendingRebootDetector = pendingRebootDetector ?? new WindowsPendingRebootDetector();
    }

    public Task<SystemSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = new SystemSnapshot(
            Environment.OSVersion.VersionString,
            RuntimeInformation.OSArchitecture.ToString(),
            _pendingRebootDetector.DetectPendingReboot() != PendingRebootState.NotPending,
            DateTimeOffset.UtcNow);

        return Task.FromResult(snapshot);
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemRestoreInfoProvider : ISystemRestoreInfoProvider
{
    public Task<SystemRestoreInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
            var disableSr = key?.GetValue("DisableSR");
            var isEnabled = disableSr is null or 0;

            return Task.FromResult(new SystemRestoreInfo(
                isEnabled,
                isEnabled
                    ? "System Restore appears to be enabled on this PC."
                    : "System Restore appears to be disabled on this PC."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SystemRestoreInfo(
                false,
                $"System Restore status could not be determined: {ex.Message}"));
        }
    }
}
