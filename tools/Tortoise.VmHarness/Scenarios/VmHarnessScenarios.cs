using System.Text.RegularExpressions;
using Tortoise.VmHarness.Domain;
using Tortoise.VmHarness.Guest;
using Tortoise.VmHarness.Safety;

namespace Tortoise.VmHarness.Scenarios;

public static partial class TortoiseCliOutputParser
{
    [GeneratedRegex(@"Plan\s+(?<id>[0-9a-fA-F-]{36})", RegexOptions.Compiled)]
    private static partial Regex PlanIdPattern();

    public static IReadOnlyList<Guid> ParsePlanIds(string output)
    {
        return PlanIdPattern()
            .Matches(output)
            .Select(match => Guid.Parse(match.Groups["id"].Value))
            .Distinct()
            .ToList();
    }
}

public abstract class VmHarnessScenarioBase : IVmHarnessScenario
{
    public abstract VmHarnessScenarioDefinition Definition { get; }

    public abstract Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default);

    protected static VmHarnessScenarioResult DryRunResult(string scenarioId, string summary) =>
        new(
            scenarioId,
            VmHarnessVerdict.DryRunComplete,
            VmHarnessMutationOutcome.NotAttempted,
            VmHarnessRestoreOutcome.Skipped,
            summary);

    protected static VmHarnessScenarioResult NoEligibleCandidate(string scenarioId, string summary) =>
        new(
            scenarioId,
            VmHarnessVerdict.NoEligibleCandidate,
            VmHarnessMutationOutcome.NotAttempted,
            VmHarnessRestoreOutcome.Skipped,
            summary);

    protected async Task<VmHarnessGuestEnvironmentProof?> TryCaptureGuestProofAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken)
    {
        if (context.GuestTransport is null || !context.GuestTransport.IsConfigured)
        {
            return null;
        }

        return await context.GuestTransport.GetEnvironmentProofAsync(context.Target.Name, cancellationToken);
    }

    protected async Task WriteGuestStatusAsync(
        VmHarnessScenarioContext context,
        VmHarnessGuestEnvironmentProof? proof,
        CancellationToken cancellationToken)
    {
        if (proof is not null)
        {
            await context.RunStore.WriteJsonArtifactAsync(
                context.Run,
                "guest-environment.json",
                proof,
                cancellationToken);
        }
    }
}

public sealed class BaselineLowRiskInstallScenario : VmHarnessScenarioBase
{
    public override VmHarnessScenarioDefinition Definition { get; } = new(
        Id: "baseline-low-risk-install",
        Title: "Baseline low-risk Windows Update install",
        Description: "Discover an eligible low-risk Windows-recommended driver in a disposable VM and exercise the real Tortoise.Lab mutation path when explicitly enabled.",
        Prerequisites:
        [
            "Hyper-V disposable VM with Tortoise.Lab installed",
            "Guest VM markers and Lab env gates enabled",
            "Applicable low-risk Windows Update driver candidate",
        ],
        InjectionPoint: "None (baseline path)",
        ExpectedTransactionStates:
        [
            "Discovered",
            "Eligible",
            "Planned",
            "PreflightRunning",
            "Verified",
            "AwaitingElevation",
            "Installing",
            "Completed or AwaitingReboot",
        ],
        ExpectedBrokerResponse: "InstallDriver succeeds or fail-closed with explicit reason",
        ExpectedMutationStatus: "Succeeded when candidate exists and all gates pass",
        ExpectedRecoveryState: "None on success; AwaitingReboot when reboot required",
        EvidenceCaptured:
        [
            "guest-environment.json",
            "plan.json",
            "preflight.json",
            "broker-result.json",
            "transactions-before.json",
            "transactions-after.json",
        ],
        RequiresRealMutation: true);

    public override async Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default)
    {
        var guestProof = await TryCaptureGuestProofAsync(context, cancellationToken);
        await WriteGuestStatusAsync(context, guestProof, cancellationToken);

        if (context.GuestTransport is null || !context.GuestTransport.IsConfigured)
        {
            return DryRunResult(
                Definition.Id,
                "Dry-run complete: guest transport is not configured; candidate discovery skipped.");
        }

        var labEnv = new Dictionary<string, string>
        {
            ["TORTOISE_MUTATION_TESTS"] = "1",
            ["TORTOISE_ALLOW_VM_INSTALL"] = "1",
        };

        var scan = await context.GuestTransport.ExecuteCommandAsync(
            context.Target.Name,
            "tortoise scan",
            labEnv,
            cancellationToken);
        await context.RunStore.WriteSanitizedLogAsync(
            context.Run,
            "guest-scan.log",
            $"{scan.StandardOutput}\n{scan.StandardError}",
            cancellationToken);

        if (scan.ExitCode != 0)
        {
            return new VmHarnessScenarioResult(
                Definition.Id,
                VmHarnessVerdict.HarnessError,
                VmHarnessMutationOutcome.NotAttempted,
                VmHarnessRestoreOutcome.Skipped,
                "Guest scan failed; cannot discover candidates.");
        }

        var planResult = await context.GuestTransport.ExecuteCommandAsync(
            context.Target.Name,
            "tortoise plan",
            labEnv,
            cancellationToken);
        await context.RunStore.WriteSanitizedLogAsync(
            context.Run,
            "guest-plan.log",
            $"{planResult.StandardOutput}\n{planResult.StandardError}",
            cancellationToken);
        await context.RunStore.WriteTextArtifactAsync(
            context.Run,
            "plan.json",
            planResult.StandardOutput,
            cancellationToken);

        var planIds = TortoiseCliOutputParser.ParsePlanIds(planResult.StandardOutput);
        if (planIds.Count == 0)
        {
            return NoEligibleCandidate(
                Definition.Id,
                "No actionable update plans were created in the guest VM.");
        }

        if (!context.ExecuteMutation)
        {
            return DryRunResult(
                Definition.Id,
                $"Dry-run complete: discovered {planIds.Count} plan(s); mutation not requested.");
        }

        var selectedPlanId = planIds[0];
        var preflight = await context.GuestTransport.ExecuteCommandAsync(
            context.Target.Name,
            $"tortoise preflight {selectedPlanId}",
            labEnv,
            cancellationToken);
        await context.RunStore.WriteTextArtifactAsync(
            context.Run,
            "preflight.json",
            $"{preflight.StandardOutput}\n{preflight.StandardError}",
            cancellationToken);

        if (preflight.ExitCode != 0)
        {
            return new VmHarnessScenarioResult(
                Definition.Id,
                VmHarnessVerdict.NoEligibleCandidate,
                VmHarnessMutationOutcome.Blocked,
                VmHarnessRestoreOutcome.Skipped,
                "Preflight did not pass for discovered plan; no mutation attempted.",
                selectedPlanId.ToString());
        }

        var install = await context.GuestTransport.ExecuteCommandAsync(
            context.Target.Name,
            $"tortoise-lab vm install {selectedPlanId}",
            labEnv,
            cancellationToken);
        await context.RunStore.WriteTextArtifactAsync(
            context.Run,
            "broker-result.json",
            $"{install.StandardOutput}\n{install.StandardError}",
            cancellationToken);

        var mutationOutcome = install.ExitCode switch
        {
            0 => VmHarnessMutationOutcome.Succeeded,
            2 => VmHarnessMutationOutcome.Blocked,
            _ => VmHarnessMutationOutcome.Failed,
        };

        var verdict = mutationOutcome switch
        {
            VmHarnessMutationOutcome.Succeeded => VmHarnessVerdict.Pass,
            VmHarnessMutationOutcome.Blocked => VmHarnessVerdict.FailClosed,
            _ => VmHarnessVerdict.HarnessError,
        };

        return new VmHarnessScenarioResult(
            Definition.Id,
            verdict,
            mutationOutcome,
            VmHarnessRestoreOutcome.NotAttempted,
            install.StandardOutput.Trim(),
            selectedPlanId.ToString());
    }
}

public sealed class CheckpointRestoreProofScenario : VmHarnessScenarioBase
{
    public override VmHarnessScenarioDefinition Definition { get; } = new(
        Id: "checkpoint-restore-proof",
        Title: "Checkpoint create/restore proof",
        Description: "Prove host checkpoint lifecycle without mutation.",
        Prerequisites: ["Hyper-V VM", "Checkpoint permissions"],
        InjectionPoint: "None",
        ExpectedTransactionStates: [],
        ExpectedBrokerResponse: "Not invoked",
        ExpectedMutationStatus: "NotAttempted",
        ExpectedRecoveryState: "None",
        EvidenceCaptured: ["checkpoint.json", "restore-result.json"],
        RequiresRealMutation: false);

    public override Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new VmHarnessScenarioResult(
            Definition.Id,
            VmHarnessVerdict.CheckpointRestoreProved,
            VmHarnessMutationOutcome.NotAttempted,
            VmHarnessRestoreOutcome.NotAttempted,
            "Checkpoint restore proof is handled by the orchestrator."));
}

internal static class FaultScenarioTemplate
{
    public static VmHarnessScenarioDefinition Create(
        string id,
        string title,
        string injectionPoint,
        bool requiresRealMutation) =>
        new(
            id,
            title,
            $"Fault-injection scenario '{id}'.",
            ["Registered harness scenario", "Explicit execution only"],
            injectionPoint,
            ["Scenario-specific"],
            "Scenario-specific",
            "Scenario-specific",
            "Scenario-specific",
            ["scenario.json", "broker-result.json"],
            requiresRealMutation);
}

public sealed class AbortBeforeElevationScenario : VmHarnessScenarioBase
{
    public override VmHarnessScenarioDefinition Definition { get; } =
        FaultScenarioTemplate.Create(
            "abort-before-elevation",
            "Abort before UAC elevation",
            "After download verification, before broker launch",
            requiresRealMutation: true);

    public override Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(DryRunResult(
            Definition.Id,
            "Scenario registered; execution requires guest orchestration hooks (not auto-run)."));
}

public sealed class BrokerLaunchFailureScenario : VmHarnessScenarioBase
{
    public override VmHarnessScenarioDefinition Definition { get; } =
        FaultScenarioTemplate.Create(
            "broker-launch-failure",
            "Broker launch failure",
            "Elevated broker launch",
            requiresRealMutation: true);

    public override Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(DryRunResult(Definition.Id, "Scenario registered; not auto-run."));
}

public sealed class StalePlanScenario : VmHarnessScenarioBase
{
    public override VmHarnessScenarioDefinition Definition { get; } =
        FaultScenarioTemplate.Create(
            "stale-plan",
            "Stale frozen plan",
            "After plan freeze, before broker install",
            requiresRealMutation: true);

    public override Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(DryRunResult(Definition.Id, "Scenario registered; not auto-run."));
}

public sealed class CandidateDisappearsScenario : VmHarnessScenarioBase
{
    public override VmHarnessScenarioDefinition Definition { get; } =
        FaultScenarioTemplate.Create(
            "candidate-disappears",
            "Candidate disappears at final verification",
            "Broker TOCTOU verification",
            requiresRealMutation: true);

    public override Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(DryRunResult(Definition.Id, "Scenario registered; not auto-run."));
}

public sealed class PreparedPackageMissingScenario : VmHarnessScenarioBase
{
    public override VmHarnessScenarioDefinition Definition { get; } =
        FaultScenarioTemplate.Create(
            "prepared-package-missing",
            "Prepared package missing",
            "InstallPrepared boundary",
            requiresRealMutation: true);

    public override Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(DryRunResult(Definition.Id, "Scenario registered; not auto-run."));
}

public sealed class PendingRebootScenario : VmHarnessScenarioBase
{
    public override VmHarnessScenarioDefinition Definition { get; } =
        FaultScenarioTemplate.Create(
            "pending-reboot",
            "Pending reboot gate",
            "Broker pending reboot check",
            requiresRealMutation: true);

    public override Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(DryRunResult(Definition.Id, "Scenario registered; not auto-run."));
}

public sealed class ServicingLockContentionScenario : VmHarnessScenarioBase
{
    public override VmHarnessScenarioDefinition Definition { get; } =
        FaultScenarioTemplate.Create(
            "servicing-lock-contention",
            "Servicing lock contention",
            "Broker servicing lock acquisition",
            requiresRealMutation: true);

    public override Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(DryRunResult(Definition.Id, "Scenario registered; not auto-run."));
}

public sealed class WrongPidClientScenario : VmHarnessScenarioBase
{
    public override VmHarnessScenarioDefinition Definition { get; } =
        FaultScenarioTemplate.Create(
            "wrong-pid-client",
            "Wrong PID broker client",
            "Broker pipe accept loop",
            requiresRealMutation: true);

    public override Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(DryRunResult(Definition.Id, "Scenario registered; not auto-run."));
}

public sealed class CrashAfterInstallingCheckpointScenario : VmHarnessScenarioBase
{
    public override VmHarnessScenarioDefinition Definition { get; } =
        FaultScenarioTemplate.Create(
            "crash-after-installing-checkpoint",
            "Crash after Installing state",
            "During broker execution",
            requiresRealMutation: true);

    public override Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(DryRunResult(Definition.Id, "Scenario registered; not auto-run."));
}

public sealed class AwaitingRebootResumeScenario : VmHarnessScenarioBase
{
    public override VmHarnessScenarioDefinition Definition { get; } =
        FaultScenarioTemplate.Create(
            "awaiting-reboot-resume",
            "Awaiting reboot resume",
            "Post-reboot recovery path",
            requiresRealMutation: true);

    public override Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(DryRunResult(Definition.Id, "Scenario registered; not auto-run."));
}

public sealed class PostInstallInconclusiveScenario : VmHarnessScenarioBase
{
    public override VmHarnessScenarioDefinition Definition { get; } =
        FaultScenarioTemplate.Create(
            "post-install-inconclusive",
            "Post-install inconclusive verification",
            "Verification phase",
            requiresRealMutation: false);

    public override Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(DryRunResult(Definition.Id, "Scenario registered; not auto-run."));
}

public sealed class VmHarnessScenarioRegistry
{
    private readonly IReadOnlyDictionary<string, IVmHarnessScenario> _scenarios;

    public VmHarnessScenarioRegistry(IEnumerable<IVmHarnessScenario> scenarios)
    {
        _scenarios = scenarios.ToDictionary(scenario => scenario.Definition.Id, StringComparer.OrdinalIgnoreCase);
    }

    public static VmHarnessScenarioRegistry CreateDefault() =>
        new(
        [
            new BaselineLowRiskInstallScenario(),
            new CheckpointRestoreProofScenario(),
            new AbortBeforeElevationScenario(),
            new BrokerLaunchFailureScenario(),
            new StalePlanScenario(),
            new CandidateDisappearsScenario(),
            new PreparedPackageMissingScenario(),
            new PendingRebootScenario(),
            new ServicingLockContentionScenario(),
            new WrongPidClientScenario(),
            new CrashAfterInstallingCheckpointScenario(),
            new AwaitingRebootResumeScenario(),
            new PostInstallInconclusiveScenario(),
        ]);

    public IVmHarnessScenario GetRequired(string scenarioId) =>
        _scenarios.TryGetValue(scenarioId, out var scenario)
            ? scenario
            : throw new KeyNotFoundException($"Unknown harness scenario '{scenarioId}'.");

    public IReadOnlyList<VmHarnessScenarioDefinition> ListDefinitions() =>
        _scenarios.Values.Select(scenario => scenario.Definition).OrderBy(d => d.Id).ToList();
}
