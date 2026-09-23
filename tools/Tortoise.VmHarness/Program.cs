using System.Runtime.Versioning;
using Tortoise.VmHarness.Domain;
using Tortoise.VmHarness.Guest;
using Tortoise.VmHarness.Orchestration;
using Tortoise.VmHarness.Providers;
using Tortoise.VmHarness.Providers.HyperV;
using Tortoise.VmHarness.Scenarios;
using Tortoise.VmHarness.Storage;

namespace Tortoise.VmHarness;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "inspect" => await RunInspectAsync(args),
                "run" => await RunScenarioAsync(args),
                "list-scenarios" => RunListScenarios(),
                _ => PrintUnknown(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Harness error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunInspectAsync(string[] args)
    {
        if (!TryGetRequiredOption(args, "--vm", out var vmName, out var error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Hyper-V harness inspect requires a Windows host.");
            return 1;
        }

        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.InspectAsync(vmName!);
        Console.WriteLine($"Inspect verdict: {result.Verdict}");
        Console.WriteLine($"Artifacts: {result.ArtifactsDirectory}");
        return 0;
    }

    private static async Task<int> RunScenarioAsync(string[] args)
    {
        if (!TryGetRequiredOption(args, "--vm", out var vmName, out var vmError))
        {
            Console.Error.WriteLine(vmError);
            return 1;
        }

        var scenarioId = GetOption(args, "--scenario") ?? "baseline-low-risk-install";
        var artifactsRoot = GetOption(args, "--artifacts-root") ?? VmHarnessConstants.DefaultArtifactsRoot;
        var executeMutation = args.Any(arg =>
            string.Equals(arg, VmHarnessConstants.ExecuteMutationFlag, StringComparison.OrdinalIgnoreCase));
        var proveCheckpoint = args.Any(arg =>
            string.Equals(arg, VmHarnessConstants.ProveCheckpointRestoreFlag, StringComparison.OrdinalIgnoreCase));

        if (executeMutation && !OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Real disposable VM mutation requires a Windows Hyper-V host.");
            return 1;
        }

        var mode = executeMutation
            ? VmHarnessRunMode.ExecuteDisposableVmMutation
            : proveCheckpoint
                ? VmHarnessRunMode.ProveCheckpointRestore
                : VmHarnessRunMode.DryRun;

        if (executeMutation)
        {
            Console.WriteLine("WARNING: --execute-disposable-vm-mutation enables real driver mutation in the target guest VM.");
            Console.WriteLine("Use only on disposable, checkpointed Hyper-V VMs.");
        }

        var options = new VmHarnessRunOptions(
            vmName!,
            scenarioId,
            mode,
            artifactsRoot,
            Evidence.VmHarnessGitMetadata.TryResolveCommitSha(),
            TimeSpan.FromMinutes(30));

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Hyper-V harness run requires a Windows host.");
            return 1;
        }

        var orchestrator = CreateOrchestrator(artifactsRoot);
        var result = await orchestrator.RunAsync(options);
        Console.WriteLine($"Verdict: {result.Verdict}");
        Console.WriteLine($"Mutation outcome: {result.MutationOutcome}");
        Console.WriteLine($"Restore outcome: {result.RestoreOutcome}");
        Console.WriteLine($"Report: {result.ReportPath}");
        return result.Verdict switch
        {
            VmHarnessVerdict.Pass => 0,
            VmHarnessVerdict.NoEligibleCandidate => 0,
            VmHarnessVerdict.DryRunComplete => 0,
            VmHarnessVerdict.CheckpointRestoreProved => 0,
            _ => 2,
        };
    }

    private static int RunListScenarios()
    {
        foreach (var definition in VmHarnessScenarioRegistry.CreateDefault().ListDefinitions())
        {
            Console.WriteLine($"{definition.Id}: {definition.Title}");
        }

        return 0;
    }

    private static VmHarnessOrchestrator CreateOrchestrator(string? artifactsRoot = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Hyper-V harness requires a Windows host.");
        }

        return CreateWindowsOrchestrator(artifactsRoot);
    }

    [SupportedOSPlatform("windows")]
    private static VmHarnessOrchestrator CreateWindowsOrchestrator(string? artifactsRoot)
    {
        IVmHarnessProvider provider = new HyperVVmHarnessProvider();
        IVmHarnessRunStore runStore = new FileSystemVmHarnessRunStore(
            artifactsRoot ?? VmHarnessConstants.DefaultArtifactsRoot);
        IVmHarnessGuestTransport guestTransport = new HyperVPowerShellDirectGuestTransport();
        return new VmHarnessOrchestrator(
            provider,
            runStore,
            guestTransport,
            VmHarnessScenarioRegistry.CreateDefault());
    }

    private static int PrintUnknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Tortoise VM Harness (developer-only, not included in public releases)");
        Console.WriteLine();
        Console.WriteLine("  tortoise-vm-harness inspect --vm <exact-hyper-v-name>");
        Console.WriteLine("  tortoise-vm-harness run --vm <exact-hyper-v-name> --scenario baseline-low-risk-install");
        Console.WriteLine("  tortoise-vm-harness run --vm <name> --scenario baseline-low-risk-install --prove-checkpoint-restore");
        Console.WriteLine("  tortoise-vm-harness run --vm <name> --scenario baseline-low-risk-install --execute-disposable-vm-mutation");
        Console.WriteLine("  tortoise-vm-harness list-scenarios");
        Console.WriteLine();
        Console.WriteLine("Default run mode is dry-run (no checkpoint, no mutation).");
        Console.WriteLine("Guest commands require TORTOISE_VM_HARNESS_GUEST_USER and TORTOISE_VM_HARNESS_GUEST_PASSWORD.");
    }

    private static bool TryGetRequiredOption(
        string[] args,
        string name,
        out string? value,
        out string error)
    {
        value = GetOption(args, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"Missing required option {name}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string? GetOption(string[] args, string name)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
            {
                return arg[(name.Length + 1)..];
            }

            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
            {
                var index = Array.IndexOf(args, arg);
                if (index >= 0 && index + 1 < args.Length)
                {
                    return args[index + 1];
                }
            }
        }

        return null;
    }
}
