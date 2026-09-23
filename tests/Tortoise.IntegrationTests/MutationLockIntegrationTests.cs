using System.Diagnostics;
using System.Runtime.Versioning;
using Tortoise.Core.Mutation;

namespace Tortoise.IntegrationTests;

[Trait("Category", "Integration")]
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

        Process? workflowHolder = null;
        Process? servicingProbe = null;
        try
        {
            workflowHolder = ProcessProbeTestSupport.StartProbe(probePath, "hold", "workflow");
            await ProcessProbeTestSupport.WaitForOutputLineAsync(
                workflowHolder,
                "HOLDING",
                ProcessProbeTestSupport.DefaultProbeTimeout);

            servicingProbe = ProcessProbeTestSupport.StartProbe(probePath, "servicing");
            var servicingOutput = await ProcessProbeTestSupport.ReadProcessOutputAsync(
                servicingProbe,
                ProcessProbeTestSupport.DefaultProbeTimeout);

            Assert.Equal(0, servicingProbe.ExitCode);
            Assert.Contains("ACQUIRED", servicingOutput, StringComparison.Ordinal);
        }
        finally
        {
            ProcessProbeTestSupport.EnsureTerminated(workflowHolder);
            ProcessProbeTestSupport.EnsureTerminated(servicingProbe);
        }
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

        Process? workflowHolder = null;
        Process? competingWorkflow = null;
        try
        {
            workflowHolder = ProcessProbeTestSupport.StartProbe(probePath, "hold", "workflow");
            await ProcessProbeTestSupport.WaitForOutputLineAsync(
                workflowHolder,
                "HOLDING",
                ProcessProbeTestSupport.DefaultProbeTimeout);

            competingWorkflow = ProcessProbeTestSupport.StartProbe(probePath, "workflow");
            var competingOutput = await ProcessProbeTestSupport.ReadProcessOutputAsync(
                competingWorkflow,
                ProcessProbeTestSupport.DefaultProbeTimeout);

            Assert.Equal(2, competingWorkflow.ExitCode);
            Assert.Contains("DENIED", competingOutput, StringComparison.Ordinal);
        }
        finally
        {
            ProcessProbeTestSupport.EnsureTerminated(workflowHolder);
            ProcessProbeTestSupport.EnsureTerminated(competingWorkflow);
        }
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
