using System.Diagnostics;
using Tortoise.VmHarness.Providers.Qemu;

namespace Tortoise.VmHarness.Tests.Qemu;

public sealed class QemuLabHostSafetyTests
{
    private static string CreateTempLabRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void IsPathWithinRoot_RejectsEscapeViaParent()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "qemu-lab-root"));
        var escaped = Path.GetFullPath("/etc/passwd");
        Assert.False(QemuLabHostSafety.IsPathWithinRoot(root, escaped));
    }

    [Fact]
    public void IsPathWithinRoot_AllowsChildPath()
    {
        var root = CreateTempLabRoot();
        try
        {
            var child = Path.Combine(root, "overlays", "run.qcow2");
            Directory.CreateDirectory(Path.GetDirectoryName(child)!);
            Assert.True(QemuLabHostSafety.IsPathWithinRoot(root, child));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_BlocksMissingLabRoot()
    {
        var result = QemuLabHostSafety.Evaluate(new QemuLabHostSafetyOptions(
            LabRoot: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        Assert.Equal(QemuLabHostSafetyVerdict.LabRootMissing, result.Verdict);
    }

    [Fact]
    public void Evaluate_BlocksPathEscape()
    {
        var root = CreateTempLabRoot();
        try
        {
            var result = QemuLabHostSafety.Evaluate(new QemuLabHostSafetyOptions(
                LabRoot: root,
                BaseImagePath: "/tmp/outside.qcow2",
                MemAvailableKbOverride: 16L * 1024 * 1024,
                DiskFreeKbOverride: 16L * 1024 * 1024));
            Assert.Equal(QemuLabHostSafetyVerdict.PathEscapesLabRoot, result.Verdict);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_BlocksInsufficientHostRam()
    {
        var root = CreateTempLabRoot();
        try
        {
            var result = QemuLabHostSafety.Evaluate(new QemuLabHostSafetyOptions(
                LabRoot: root,
                MemAvailableKbOverride: 1024,
                DiskFreeKbOverride: 16L * 1024 * 1024,
                RequireKvm: false,
                AllowTcgFallback: true));
            Assert.Equal(QemuLabHostSafetyVerdict.InsufficientHostRam, result.Verdict);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_BlocksInsufficientDisk()
    {
        var root = CreateTempLabRoot();
        try
        {
            var result = QemuLabHostSafety.Evaluate(new QemuLabHostSafetyOptions(
                LabRoot: root,
                MemAvailableKbOverride: 16L * 1024 * 1024,
                DiskFreeKbOverride: 1024,
                RequireKvm: false,
                AllowTcgFallback: true));
            Assert.Equal(QemuLabHostSafetyVerdict.InsufficientDisk, result.Verdict);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_BlocksLabSizeCeilingExceeded()
    {
        var root = CreateTempLabRoot();
        try
        {
            var result = QemuLabHostSafety.Evaluate(new QemuLabHostSafetyOptions(
                LabRoot: root,
                MemAvailableKbOverride: 16L * 1024 * 1024,
                DiskFreeKbOverride: 16L * 1024 * 1024,
                LabDirectorySizeKbOverride: 50L * 1024 * 1024,
                RequireKvm: false,
                AllowTcgFallback: true));
            Assert.Equal(QemuLabHostSafetyVerdict.LabSizeCeilingExceeded, result.Verdict);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_BlocksActiveRunConflict()
    {
        var root = CreateTempLabRoot();
        try
        {
            var pidFile = Path.Combine(root, "qemu.pid");
            File.WriteAllText(pidFile, Process.GetCurrentProcess().Id.ToString());

            var result = QemuLabHostSafety.Evaluate(new QemuLabHostSafetyOptions(
                LabRoot: root,
                ActiveRunPidFilePath: pidFile,
                MemAvailableKbOverride: 16L * 1024 * 1024,
                DiskFreeKbOverride: 16L * 1024 * 1024,
                RequireKvm: false,
                AllowTcgFallback: true));
            Assert.Equal(QemuLabHostSafetyVerdict.ActiveRunConflict, result.Verdict);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_BlocksForbiddenPassthroughEnv()
    {
        var root = CreateTempLabRoot();
        try
        {
            Environment.SetEnvironmentVariable("TORTOISE_QEMU_RAW_DISK", "/dev/sda");
            var result = QemuLabHostSafety.Evaluate(new QemuLabHostSafetyOptions(
                LabRoot: root,
                MemAvailableKbOverride: 16L * 1024 * 1024,
                DiskFreeKbOverride: 16L * 1024 * 1024,
                RequireKvm: false,
                AllowTcgFallback: true));
            Assert.Equal(QemuLabHostSafetyVerdict.ForbiddenPassthroughConfigured, result.Verdict);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TORTOISE_QEMU_RAW_DISK", null);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_BlocksInvalidForwardedPort()
    {
        var root = CreateTempLabRoot();
        try
        {
            var result = QemuLabHostSafety.Evaluate(new QemuLabHostSafetyOptions(
                LabRoot: root,
                ForwardedPort: 80,
                MemAvailableKbOverride: 16L * 1024 * 1024,
                DiskFreeKbOverride: 16L * 1024 * 1024,
                RequireKvm: false,
                AllowTcgFallback: true));
            Assert.Equal(QemuLabHostSafetyVerdict.LocalhostPortBindingInvalid, result.Verdict);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_BlocksMissingQemuExecutable()
    {
        var root = CreateTempLabRoot();
        try
        {
            var result = QemuLabHostSafety.Evaluate(new QemuLabHostSafetyOptions(
                LabRoot: root,
                QemuExecutablePath: Path.Combine(root, "missing-qemu"),
                MemAvailableKbOverride: 16L * 1024 * 1024,
                DiskFreeKbOverride: 16L * 1024 * 1024,
                RequireKvm: false,
                AllowTcgFallback: true));
            Assert.Equal(QemuLabHostSafetyVerdict.QemuExecutableMissing, result.Verdict);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_BlocksTcgWhenNotAllowed()
    {
        var root = CreateTempLabRoot();
        try
        {
            var result = QemuLabHostSafety.Evaluate(new QemuLabHostSafetyOptions(
                LabRoot: root,
                MemAvailableKbOverride: 16L * 1024 * 1024,
                DiskFreeKbOverride: 16L * 1024 * 1024,
                RequireKvm: false,
                AllowTcgFallback: false));
            Assert.Equal(QemuLabHostSafetyVerdict.TcgNotAllowed, result.Verdict);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_BlocksBackingMismatch()
    {
        var root = CreateTempLabRoot();
        try
        {
            var baseDir = Path.Combine(root, "base");
            var overlayDir = Path.Combine(root, "overlays");
            Directory.CreateDirectory(baseDir);
            Directory.CreateDirectory(overlayDir);
            var baseImage = Path.Combine(baseDir, "windows-base.qcow2");
            var overlay = Path.Combine(overlayDir, "run.qcow2");
            File.WriteAllText(baseImage, "base");
            File.WriteAllText(overlay, $"backing file={Path.Combine(root, "wrong-base.qcow2")}\n");

            var result = QemuLabHostSafety.Evaluate(new QemuLabHostSafetyOptions(
                LabRoot: root,
                BaseImagePath: baseImage,
                OverlayImagePath: overlay,
                ExpectedBackingPath: baseImage,
                MemAvailableKbOverride: 16L * 1024 * 1024,
                DiskFreeKbOverride: 16L * 1024 * 1024,
                RequireKvm: false,
                AllowTcgFallback: true));

            Assert.Equal(QemuLabHostSafetyVerdict.BackingImageMismatch, result.Verdict);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_AllowsMatchingBacking()
    {
        var root = CreateTempLabRoot();
        try
        {
            var baseDir = Path.Combine(root, "base");
            var overlayDir = Path.Combine(root, "overlays");
            Directory.CreateDirectory(baseDir);
            Directory.CreateDirectory(overlayDir);
            var baseImage = Path.Combine(baseDir, "windows-base.qcow2");
            var overlay = Path.Combine(overlayDir, "run.qcow2");
            File.WriteAllText(baseImage, "base");
            QemuOverlayManager.CreateOverlay(baseImage, overlay);

            var result = QemuLabHostSafety.Evaluate(new QemuLabHostSafetyOptions(
                LabRoot: root,
                BaseImagePath: baseImage,
                OverlayImagePath: overlay,
                ExpectedBackingPath: baseImage,
                MemAvailableKbOverride: 16L * 1024 * 1024,
                DiskFreeKbOverride: 16L * 1024 * 1024,
                RequireKvm: false,
                AllowTcgFallback: true));

            Assert.Equal(QemuLabHostSafetyVerdict.Ok, result.Verdict);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BaseImageFingerprint_DetectsMutation()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".img");
        try
        {
            File.WriteAllText(path, "pristine");
            var original = QemuBaseImageFingerprint.ComputeSha256(path);
            Assert.True(QemuBaseImageFingerprint.Matches(path, original));

            File.AppendAllText(path, "mutated");
            Assert.False(QemuBaseImageFingerprint.Matches(path, original));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RestoreCheckpoint_DiscardsOverlayWithinLabRoot()
    {
        var root = CreateTempLabRoot();
        try
        {
            var baseImage = Path.Combine(root, "base", "windows-base.qcow2");
            Directory.CreateDirectory(Path.GetDirectoryName(baseImage)!);
            File.WriteAllText(baseImage, "base");

            var provider = new QemuVmHarnessProvider(new QemuLabConfiguration(
                root,
                baseImage,
                Path.Combine(root, "overlays"),
                RequireKvm: false,
                AllowTcgFallback: true));

            var target = await provider.ResolveVmAsync("run-123");
            var overlay = await provider.CreateCheckpointAsync(target, "run-123");
            Assert.True(File.Exists(overlay.Id));

            var restore = await provider.RestoreCheckpointAsync(target, overlay);
            Assert.True(restore.Succeeded);
            Assert.False(File.Exists(overlay.Id));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreCheckpoint_RefusesPathOutsideLabRoot()
    {
        var root = CreateTempLabRoot();
        try
        {
            var provider = new QemuVmHarnessProvider(new QemuLabConfiguration(
                root,
                Path.Combine(root, "base", "windows-base.qcow2"),
                Path.Combine(root, "overlays"),
                RequireKvm: false,
                AllowTcgFallback: true));

            var target = new Tortoise.VmHarness.Domain.VmHarnessVmTarget("run", "run", "Stopped", true);
            var outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".qcow2");
            File.WriteAllText(outside, "overlay");
            var checkpoint = new Tortoise.VmHarness.Domain.VmHarnessCheckpoint(
                "bad",
                outside,
                DateTimeOffset.UtcNow,
                "run",
                "run");

            var restore = await provider.RestoreCheckpointAsync(target, checkpoint);
            Assert.False(restore.Succeeded);
            Assert.Contains("lab root", restore.Summary, StringComparison.OrdinalIgnoreCase);
            File.Delete(outside);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreCheckpoint_BlocksRunIdMismatch()
    {
        var root = CreateTempLabRoot();
        try
        {
            var provider = new QemuVmHarnessProvider(new QemuLabConfiguration(
                root,
                Path.Combine(root, "base", "windows-base.qcow2"),
                Path.Combine(root, "overlays"),
                RequireKvm: false,
                AllowTcgFallback: true));

            var target = new Tortoise.VmHarness.Domain.VmHarnessVmTarget("run-a", "run-a", "Stopped", true);
            var overlayPath = Path.Combine(root, "overlays", "run-b.qcow2");
            Directory.CreateDirectory(Path.GetDirectoryName(overlayPath)!);
            File.WriteAllText(overlayPath, "overlay");
            var checkpoint = new Tortoise.VmHarness.Domain.VmHarnessCheckpoint(
                "run-b",
                overlayPath,
                DateTimeOffset.UtcNow,
                "run-b",
                "run-b");

            var restore = await provider.RestoreCheckpointAsync(target, checkpoint);
            Assert.False(restore.Succeeded);
            Assert.Contains("run ID", restore.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
