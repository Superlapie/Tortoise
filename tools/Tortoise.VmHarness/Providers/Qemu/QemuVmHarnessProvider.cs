using Tortoise.VmHarness.Domain;

namespace Tortoise.VmHarness.Providers.Qemu;

/// <summary>
/// QEMU/KVM harness provider using disposable qcow2 overlays over a sealed base image.
/// Overlay discard/reset is the QEMU equivalent of checkpoint restore — not Hyper-V checkpoint semantics.
/// </summary>
public sealed class QemuVmHarnessProvider : IVmHarnessProvider
{
    private readonly QemuLabHostSafetyOptions _safetyOptions;
    private readonly string _baseImagePath;
    private readonly string _overlaysDirectory;

    public QemuVmHarnessProvider(QemuLabConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _baseImagePath = configuration.BaseImagePath;
        _overlaysDirectory = configuration.OverlaysDirectory;
        _safetyOptions = new QemuLabHostSafetyOptions(
            LabRoot: configuration.LabRoot,
            BaseImagePath: configuration.BaseImagePath,
            RequireKvm: configuration.RequireKvm,
            AllowTcgFallback: configuration.AllowTcgFallback);
    }

    public string ProviderName => "QEMU/KVM";

    public Task EnsureHostRequirementsAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new VmHarnessProviderException("QEMU harness provider requires a Linux host.");
        }

        var safety = QemuLabHostSafety.Evaluate(_safetyOptions);
        if (safety.Verdict != QemuLabHostSafetyVerdict.Ok)
        {
            throw new VmHarnessProviderException($"Host safety blocked QEMU lab: {safety.Message}");
        }

        if (!File.Exists(_baseImagePath))
        {
            throw new VmHarnessProviderException(
                $"Sealed base image not found at '{_baseImagePath}'. Install/seal Windows base before overlay proofs.");
        }

        return Task.CompletedTask;
    }

    public Task<VmHarnessVmTarget> ResolveVmAsync(string vmName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vmName);
        if (!QemuLabHostSafety.IsPathWithinRoot(_safetyOptions.LabRoot, _overlaysDirectory))
        {
            throw new VmHarnessProviderException("Overlay directory escapes lab root.");
        }

        // vmName is the lab profile/run label; HyperVVmId holds immutable run id for QEMU binding.
        var runId = vmName.Trim();
        return Task.FromResult(new VmHarnessVmTarget(
            runId,
            HyperVVmId: runId,
            State: "Configured",
            CheckpointOperationsAvailable: File.Exists(_baseImagePath)));
    }

    public Task<VmHarnessVmTarget> VerifyTargetIdentityAsync(
        VmHarnessVmTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(target.HyperVVmId)
            || !string.Equals(target.Name, target.HyperVVmId, StringComparison.Ordinal))
        {
            throw new VmHarnessProviderException("QEMU target identity binding mismatch.");
        }

        return Task.FromResult(target);
    }

    public Task<VmHarnessCheckpoint> CreateCheckpointAsync(
        VmHarnessVmTarget target,
        string checkpointName,
        CancellationToken cancellationToken = default)
    {
        // QEMU overlay creation — disposable run image backed by sealed base.
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointName);

        Directory.CreateDirectory(_overlaysDirectory);
        var overlayPath = Path.Combine(_overlaysDirectory, $"{checkpointName}.qcow2");
        if (!QemuLabHostSafety.IsPathWithinRoot(_safetyOptions.LabRoot, overlayPath))
        {
            throw new VmHarnessProviderException("Overlay path escapes lab root.");
        }

        if (File.Exists(overlayPath))
        {
            throw new VmHarnessProviderException($"Overlay already exists: {overlayPath}");
        }

        QemuOverlayManager.CreateOverlay(_baseImagePath, overlayPath);

        var safety = QemuLabHostSafety.Evaluate(_safetyOptions with
        {
            OverlayImagePath = overlayPath,
            ExpectedBackingPath = _baseImagePath,
        });

        if (safety.Verdict != QemuLabHostSafetyVerdict.Ok)
        {
            TryDeleteFile(overlayPath);
            throw new VmHarnessProviderException($"Overlay safety validation failed: {safety.Message}");
        }

        return Task.FromResult(new VmHarnessCheckpoint(
            checkpointName,
            Id: overlayPath,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            HyperVVmId: target.HyperVVmId,
            VmName: target.Name));
    }

    public Task<VmHarnessRestoreResult> RestoreCheckpointAsync(
        VmHarnessVmTarget target,
        VmHarnessCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        // Overlay discard — delete disposable overlay; base remains sealed.
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (!string.Equals(checkpoint.HyperVVmId, target.HyperVVmId, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new VmHarnessRestoreResult(
                false,
                checkpoint.Name,
                checkpoint.Id,
                false,
                false,
                "Overlay run ID does not match target.",
                "Overlay/target ID mismatch."));
        }

        if (checkpoint.Id is null || !File.Exists(checkpoint.Id))
        {
            return Task.FromResult(new VmHarnessRestoreResult(
                true,
                checkpoint.Name,
                checkpoint.Id,
                false,
                false,
                "Overlay already absent; nothing to discard."));
        }

        if (!QemuLabHostSafety.IsPathWithinRoot(_safetyOptions.LabRoot, checkpoint.Id))
        {
            return Task.FromResult(new VmHarnessRestoreResult(
                false,
                checkpoint.Name,
                checkpoint.Id,
                false,
                false,
                "Refusing to discard path outside lab root.",
                "Overlay path escapes lab root."));
        }

        TryDeleteFile(checkpoint.Id);
        var discarded = !File.Exists(checkpoint.Id);
        return Task.FromResult(new VmHarnessRestoreResult(
            discarded,
            checkpoint.Name,
            checkpoint.Id,
            HyperVStateRunning: false,
            GuestReachable: false,
            discarded
                ? "Disposable overlay discarded; sealed base unchanged."
                : "Overlay discard failed."));
    }

    public Task<bool> WaitForHyperVRunningAsync(
        VmHarnessVmTarget target,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Caller interprets restore result.
        }
    }
}

public sealed record QemuLabConfiguration(
    string LabRoot,
    string BaseImagePath,
    string OverlaysDirectory,
    bool RequireKvm = true,
    bool AllowTcgFallback = false);

public static class QemuBaseImageFingerprint
{
    public static string ComputeSha256(string imagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        using var stream = File.OpenRead(imagePath);
        var hash = System.Security.Cryptography.SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool Matches(string imagePath, string expectedSha256Hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256Hex);
        if (!File.Exists(imagePath))
        {
            return false;
        }

        var actual = ComputeSha256(imagePath);
        return string.Equals(actual, expectedSha256Hex, StringComparison.OrdinalIgnoreCase);
    }
}

public static class QemuOverlayManager
{
    public static void CreateOverlay(string baseImagePath, string overlayPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseImagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(overlayPath);

        var directory = Path.GetDirectoryName(overlayPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Minimal qcow2 overlay marker for unit tests without invoking qemu-img.
        // Production host scripts use `qemu-img create -f qcow2 -F qcow2 -b ...`.
        File.WriteAllText(
            overlayPath,
            $"backing file={Path.GetFullPath(baseImagePath)}\n# tortoise-qemu-overlay\n");
    }
}
