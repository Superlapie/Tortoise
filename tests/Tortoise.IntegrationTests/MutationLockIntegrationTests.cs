using System.Diagnostics;
using System.Runtime.Versioning;
using Tortoise.Core.Mutation;

namespace Tortoise.IntegrationTests;

public sealed class MutationLockIntegrationTests
{
    [Fact]
    public async Task Workflow_lock_does_not_block_servicing_lock_in_separate_process()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var probePath = LocateMutationLockProbe();
        if (probePath is null)
        {
            throw new InvalidOperationException("tortoise-mutation-lock-probe was not found.");
        }

        using var workflowHolder = StartProbe(probePath, "hold", "workflow");
        await WaitForOutputLineAsync(workflowHolder, "HOLDING");

        using var servicingProbe = StartProbe(probePath, "servicing");
        var servicingOutput = await servicingProbe.StandardOutput.ReadToEndAsync();
        await servicingProbe.WaitForExitAsync();

        Assert.Equal(0, servicingProbe.ExitCode);
        Assert.Contains("ACQUIRED", servicingOutput, StringComparison.Ordinal);

        workflowHolder.Kill(entireProcessTree: true);
        await workflowHolder.WaitForExitAsync();
    }

    [Fact]
    public async Task Second_workflow_lock_is_denied_while_first_is_held()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var probePath = LocateMutationLockProbe();
        if (probePath is null)
        {
            throw new InvalidOperationException("tortoise-mutation-lock-probe was not found.");
        }

        using var workflowHolder = StartProbe(probePath, "hold", "workflow");
        await WaitForOutputLineAsync(workflowHolder, "HOLDING");

        using var competingWorkflow = StartProbe(probePath, "workflow");
        var competingOutput = await competingWorkflow.StandardOutput.ReadToEndAsync();
        await competingWorkflow.WaitForExitAsync();

        Assert.Equal(2, competingWorkflow.ExitCode);
        Assert.Contains("DENIED", competingOutput, StringComparison.Ordinal);

        workflowHolder.Kill(entireProcessTree: true);
        await workflowHolder.WaitForExitAsync();
    }

    private static Process StartProbe(string probePath, params string[] args)
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{probePath}\" {string.Join(' ', args)}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Failed to start mutation lock probe.");
    }

    private static async Task WaitForOutputLineAsync(Process process, string expectedLine)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line is not null && line.Contains(expectedLine, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Timed out waiting for probe output containing '{expectedLine}'.");
    }

    private static string? LocateMutationLockProbe()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tortoise-mutation-lock-probe.dll"),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "Tortoise.MutationLockProbe",
                "bin",
                "Debug",
                "net10.0",
                "tortoise-mutation-lock-probe.dll")),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
