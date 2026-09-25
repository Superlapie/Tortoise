namespace Tortoise.VmHarness.Providers.Qemu;

public enum QemuLabHostSafetyVerdict
{
    Ok = 0,
    LabRootMissing = 1,
    InsufficientHostRam = 2,
    InsufficientDisk = 3,
    LabSizeCeilingExceeded = 4,
    ActiveRunConflict = 5,
    KvmUnavailable = 6,
    TcgNotAllowed = 7,
    PathEscapesLabRoot = 8,
    ForbiddenPassthroughConfigured = 9,
    BackingImageMismatch = 10,
    BaseImageMissing = 11,
    QemuExecutableMissing = 12,
    LocalhostPortBindingInvalid = 13,
}

public sealed record QemuLabHostSafetyOptions(
    string LabRoot,
    long MinFreeRamKb = 8L * 1024 * 1024,
    long MinFreeDiskKb = 10L * 1024 * 1024,
    long MaxLabDirectoryKb = 45L * 1024 * 1024,
    bool RequireKvm = true,
    bool AllowTcgFallback = false,
    string? BaseImagePath = null,
    string? OverlayImagePath = null,
    string? ExpectedBackingPath = null,
    int? ForwardedPort = null,
    string? QemuExecutablePath = null,
    string? ActiveRunPidFilePath = null,
    long? MemAvailableKbOverride = null,
    long? DiskFreeKbOverride = null,
    long? LabDirectorySizeKbOverride = null);

public sealed record QemuLabHostSafetyResult(
    QemuLabHostSafetyVerdict Verdict,
    string Message,
    long? MemAvailableKb = null,
    long? DiskFreeKb = null,
    long? LabDirectoryKb = null);

public static class QemuLabHostSafety
{
    public static QemuLabHostSafetyResult Evaluate(QemuLabHostSafetyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LabRoot);

        if (!Directory.Exists(options.LabRoot))
        {
            return Block(QemuLabHostSafetyVerdict.LabRootMissing, $"Lab root does not exist: {options.LabRoot}");
        }

        var labRoot = Path.GetFullPath(options.LabRoot);

        if (options.BaseImagePath is not null
            && !IsPathWithinRoot(labRoot, options.BaseImagePath))
        {
            return Block(
                QemuLabHostSafetyVerdict.PathEscapesLabRoot,
                $"Base image escapes lab root: {options.BaseImagePath}");
        }

        if (options.OverlayImagePath is not null
            && !IsPathWithinRoot(labRoot, options.OverlayImagePath))
        {
            return Block(
                QemuLabHostSafetyVerdict.PathEscapesLabRoot,
                $"Overlay image escapes lab root: {options.OverlayImagePath}");
        }

        foreach (var forbidden in new[] { "TORTOISE_QEMU_HOST_PASSTHROUGH", "TORTOISE_QEMU_VIRTFS", "TORTOISE_QEMU_RAW_DISK" })
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(forbidden)))
            {
                return Block(
                    QemuLabHostSafetyVerdict.ForbiddenPassthroughConfigured,
                    $"Forbidden environment variable is set: {forbidden}");
            }
        }

        if (options.ForwardedPort is int port && (port < 1024 || port > 65535))
        {
            return Block(
                QemuLabHostSafetyVerdict.LocalhostPortBindingInvalid,
                $"Forwarded port {port} must be between 1024 and 65535 (localhost-only binding enforced by launch scripts).");
        }

        var memAvailableKb = options.MemAvailableKbOverride ?? TryReadMemAvailableKb();
        if (memAvailableKb is not null && memAvailableKb.Value < options.MinFreeRamKb)
        {
            return Block(
                QemuLabHostSafetyVerdict.InsufficientHostRam,
                $"MemAvailable {memAvailableKb.Value}KB is below required {options.MinFreeRamKb}KB.");
        }

        var diskFreeKb = options.DiskFreeKbOverride ?? TryReadDiskFreeKb(labRoot);
        if (diskFreeKb is not null && diskFreeKb.Value < options.MinFreeDiskKb)
        {
            return Block(
                QemuLabHostSafetyVerdict.InsufficientDisk,
                $"Free disk on lab filesystem is {diskFreeKb.Value}KB; required {options.MinFreeDiskKb}KB.");
        }

        var labDirectoryKb = options.LabDirectorySizeKbOverride ?? TryReadDirectorySizeKb(labRoot);
        if (labDirectoryKb is not null && labDirectoryKb.Value > options.MaxLabDirectoryKb)
        {
            return Block(
                QemuLabHostSafetyVerdict.LabSizeCeilingExceeded,
                $"Lab directory size {labDirectoryKb.Value}KB exceeds ceiling {options.MaxLabDirectoryKb}KB.");
        }

        if (options.ActiveRunPidFilePath is not null
            && File.Exists(options.ActiveRunPidFilePath)
            && TryReadPidIsAlive(options.ActiveRunPidFilePath))
        {
            return Block(
                QemuLabHostSafetyVerdict.ActiveRunConflict,
                $"Another Tortoise lab QEMU instance appears active ({options.ActiveRunPidFilePath}).");
        }

        if (options.QemuExecutablePath is not null
            && !File.Exists(options.QemuExecutablePath))
        {
            return Block(
                QemuLabHostSafetyVerdict.QemuExecutableMissing,
                $"QEMU executable not found: {options.QemuExecutablePath}");
        }

        if (options.BaseImagePath is not null && !File.Exists(options.BaseImagePath))
        {
            return Block(
                QemuLabHostSafetyVerdict.BaseImageMissing,
                $"Base image not found: {options.BaseImagePath}");
        }

        if (options.ExpectedBackingPath is not null
            && options.OverlayImagePath is not null
            && File.Exists(options.OverlayImagePath))
        {
            var backing = QemuOverlayInspector.TryReadBackingFile(options.OverlayImagePath);
            if (backing is null)
            {
                return Block(
                    QemuLabHostSafetyVerdict.BackingImageMismatch,
                    $"Could not read backing file for overlay: {options.OverlayImagePath}");
            }

            var expected = Path.GetFullPath(options.ExpectedBackingPath);
            var actual = Path.GetFullPath(backing);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                return Block(
                    QemuLabHostSafetyVerdict.BackingImageMismatch,
                    $"Overlay backing mismatch. Expected '{expected}', got '{actual}'.");
            }
        }

        if (options.RequireKvm)
        {
            if (!File.Exists("/dev/kvm"))
            {
                return Block(QemuLabHostSafetyVerdict.KvmUnavailable, "/dev/kvm is not present.");
            }

            if (!CanAccessKvmDevice())
            {
                return Block(
                    QemuLabHostSafetyVerdict.KvmUnavailable,
                    "Current user cannot access /dev/kvm (group membership required).");
            }
        }
        else if (!options.AllowTcgFallback)
        {
            return Block(
                QemuLabHostSafetyVerdict.TcgNotAllowed,
                "KVM is not required but TCG fallback is not explicitly allowed.");
        }

        return new QemuLabHostSafetyResult(
            QemuLabHostSafetyVerdict.Ok,
            "Host safety checks passed.",
            memAvailableKb,
            diskFreeKb,
            labDirectoryKb);
    }

    private static long? TryReadMemAvailableKb()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/proc/meminfo"))
        {
            return null;
        }

        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2 && long.TryParse(parts[1], out var kb) ? kb : null;
            }
        }

        return null;
    }

    private static long? TryReadDiskFreeKb(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace / 1024;
        }
        catch
        {
            return null;
        }
    }

    private static long? TryReadDirectorySizeKb(string path)
    {
        try
        {
            long totalBytes = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                totalBytes += new FileInfo(file).Length;
            }

            return totalBytes / 1024;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadPidIsAlive(string pidFilePath)
    {
        try
        {
            var text = File.ReadAllText(pidFilePath).Trim();
            if (!int.TryParse(text, out var pid) || pid <= 0)
            {
                return false;
            }

            return File.Exists($"/proc/{pid}/stat");
        }
        catch
        {
            return false;
        }
    }

    public static bool IsPathWithinRoot(string labRoot, string candidatePath)
    {
        var root = Path.GetFullPath(labRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(candidatePath);
        return candidate.StartsWith(root, StringComparison.Ordinal)
            || string.Equals(candidate, root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal);
    }

    private static QemuLabHostSafetyResult Block(QemuLabHostSafetyVerdict verdict, string message) =>
        new(verdict, message);

    private static bool CanAccessKvmDevice()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            using var stream = File.Open("/dev/kvm", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}

public static class QemuOverlayInspector
{
    public static string? TryReadBackingFile(string overlayPath)
    {
        if (!File.Exists(overlayPath))
        {
            return null;
        }

        foreach (var line in File.ReadLines(overlayPath))
        {
            if (line.StartsWith("backing file=", StringComparison.Ordinal))
            {
                var value = line["backing file=".Length..].Trim();
                if (value.StartsWith("..", StringComparison.Ordinal))
                {
                    var dir = Path.GetDirectoryName(overlayPath) ?? ".";
                    return Path.GetFullPath(Path.Combine(dir, value));
                }

                return Path.GetFullPath(value);
            }
        }

        return null;
    }
}
