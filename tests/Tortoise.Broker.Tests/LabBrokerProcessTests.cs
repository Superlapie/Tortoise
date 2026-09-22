using System.Diagnostics;
using Tortoise.Broker.Validation;
using Tortoise.Contracts.Mutation;

namespace Tortoise.Broker.Tests;

public sealed class LabBrokerProcessTests
{
    [Fact]
    public async Task LabBroker_process_is_lab_role_and_fails_closed_without_vm_markers()
    {
        var brokerPath = LocateLabBrokerAssembly();
        if (brokerPath is null)
        {
            throw new InvalidOperationException("tortoise-lab-broker assembly was not found for process smoke testing.");
        }

        var databasePath = Path.Combine(TortoiseLabPaths.LabDataRoot, "process-smoke", "tortoise.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await File.WriteAllBytesAsync(databasePath, [0x53, 0x51, 0x4C, 0x69, 0x74, 0x65, 0x20, 0x66, 0x6F, 0x72, 0x6D, 0x61, 0x74, 0x20, 0x33, 0x00]);
        LabAuthoritativeStoreGuard.EnsureProtectedStoreReady(databasePath);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments =
                $"\"{brokerPath}\" serve --session-id=1 --client-pid={Environment.ProcessId} --db=\"{databasePath}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Failed to start tortoise-lab-broker process.");

        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.NotEqual(2, process.ExitCode);
        Assert.Equal(3, process.ExitCode);
        Assert.Contains("mutation capability is not enabled", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LabBroker_process_requires_client_pid()
    {
        var brokerPath = LocateLabBrokerAssembly();
        if (brokerPath is null)
        {
            throw new InvalidOperationException("tortoise-lab-broker assembly was not found for process smoke testing.");
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{brokerPath}\" serve --session-id=1",
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Failed to start tortoise-lab-broker process.");

        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(5, process.ExitCode);
        Assert.Contains("--client-pid", stderr, StringComparison.OrdinalIgnoreCase);
    }

    private static string? LocateLabBrokerAssembly()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tortoise-lab-broker.dll"),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "Tortoise.LabBroker",
                "bin",
                "Debug",
                "net10.0",
                "tortoise-lab-broker.dll")),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
