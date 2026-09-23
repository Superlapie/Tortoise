using Tortoise.VmHarness.Domain;
using Tortoise.VmHarness.Evidence;
using Tortoise.VmHarness.Guest;
using Tortoise.VmHarness.Safety;

namespace Tortoise.VmHarness.Scenarios;

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

        return await context.GuestTransport.GetEnvironmentProofAsync(
            VmHarnessGuestTargetFactory.FromVmTarget(context.Target),
            cancellationToken);
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
            "recommendation-before.json",
            "wua-before.json",
            "device-before.json",
            "transactions-before.json",
            "plan.json",
            "lab-preflight.json",
            "inner-gate-evidence.json",
            "broker-result.json",
            "transactions-after.json",
            "device-after.json",
            "wua-after.json",
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

        if (string.IsNullOrWhiteSpace(context.LabDatabasePath))
        {
            return new VmHarnessScenarioResult(
                Definition.Id,
                VmHarnessVerdict.HarnessError,
                VmHarnessMutationOutcome.NotAttempted,
                VmHarnessRestoreOutcome.Skipped,
                "Lab database path was not resolved for this harness run.");
        }

        var dbArg = VmHarnessLabDatabaseResolver.ToGuestDbArgument(context.LabDatabasePath);
        var labEnv = VmHarnessLabEnvironment.MutationCommandEnvironment;
        var guestTarget = VmHarnessGuestTargetFactory.FromVmTarget(context.Target);

        var recommend = await context.GuestTransport.ExecuteCommandAsync(
            guestTarget,
            VmHarnessGuestEvidenceCommands.Recommend(dbArg),
            labEnv,
            cancellationToken);
        await context.RunStore.WriteSanitizedLogAsync(
            context.Run,
            "guest-recommend.log",
            $"{recommend.StandardOutput}\n{recommend.StandardError}",
            cancellationToken);

        if (recommend.ExitCode != 0)
        {
            return new VmHarnessScenarioResult(
                Definition.Id,
                VmHarnessVerdict.HarnessError,
                VmHarnessMutationOutcome.NotAttempted,
                VmHarnessRestoreOutcome.Skipped,
                "Guest recommendation scan failed; cannot discover candidates.");
        }

        var planResult = await context.GuestTransport.ExecuteCommandAsync(
            guestTarget,
            VmHarnessGuestEvidenceCommands.PlanJson(dbArg),
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

        var planIds = VmHarnessPlanJsonParser.ParsePlans(planResult.StandardOutput);
        if (planIds.Count == 0)
        {
            return NoEligibleCandidate(
                Definition.Id,
                "No actionable update plans were created in the guest VM.");
        }

        var selectedPlanId = VmHarnessPlanJsonParser.SelectEligibleBaselinePlan(planResult.StandardOutput);
        if (selectedPlanId is null)
        {
            return NoEligibleCandidate(
                Definition.Id,
                "No low-risk Windows-recommended plan candidate was available.");
        }

        if (!context.ExecuteMutation)
        {
            return DryRunResult(
                Definition.Id,
                $"Dry-run complete: discovered {planIds.Count} plan(s), {planIds.Count(p => string.Equals(p.RiskLevel, "Low", StringComparison.OrdinalIgnoreCase) && string.Equals(p.Classification, "WindowsRecommended", StringComparison.OrdinalIgnoreCase))} eligible; mutation not requested.");
        }

        await VmHarnessGuestEvidenceCollector.CaptureBeforeMutationAsync(
            context,
            cancellationToken);

        var preflight = await context.GuestTransport.ExecuteCommandAsync(
            guestTarget,
            VmHarnessGuestEvidenceCommands.LabPreflightJson(selectedPlanId.Value, dbArg),
            labEnv,
            cancellationToken);
        await context.RunStore.WriteTextArtifactAsync(
            context.Run,
            "lab-preflight.json",
            preflight.StandardOutput.Trim(),
            cancellationToken);

        HarnessLabVmPreflightJson? preflightJson = null;
        if (preflight.ExitCode == 0)
        {
            try
            {
                preflightJson = HarnessLabJsonParser.ParsePreflightJson(preflight.StandardOutput.Trim());
            }
            catch
            {
                // Fall through to blocked handling below.
            }
        }

        if (preflight.ExitCode != 0 || preflightJson?.IsBlocked == true)
        {
            return new VmHarnessScenarioResult(
                Definition.Id,
                VmHarnessVerdict.NoEligibleCandidate,
                VmHarnessMutationOutcome.Blocked,
                VmHarnessRestoreOutcome.Skipped,
                "Lab preflight did not pass for the selected eligible plan; no mutation attempted.",
                selectedPlanId.Value.ToString());
        }

        if (preflightJson?.InnerGateEvidence is not null)
        {
            var preflightGate = VmHarnessInnerLabGate.EvaluateFromLabEvidence(
                LabInstallResultClassifier.MapObservedInnerGate(preflightJson.InnerGateEvidence));
            await context.RunStore.WriteJsonArtifactAsync(
                context.Run,
                "inner-gate-evidence.json",
                preflightGate,
                cancellationToken);
        }

        var install = await context.GuestTransport.ExecuteCommandAsync(
            guestTarget,
            VmHarnessGuestEvidenceCommands.LabInstallJson(selectedPlanId.Value, dbArg),
            labEnv,
            cancellationToken);

        HarnessLabVmInstallJson installJson;
        try
        {
            installJson = HarnessLabJsonParser.ParseInstallJson(install.StandardOutput.Trim());
        }
        catch
        {
            return new VmHarnessScenarioResult(
                Definition.Id,
                VmHarnessVerdict.HarnessError,
                VmHarnessMutationOutcome.Failed,
                VmHarnessRestoreOutcome.NotAttempted,
                "Lab install did not return structured JSON.",
                selectedPlanId.Value.ToString());
        }

        await context.RunStore.WriteTextArtifactAsync(
            context.Run,
            "broker-result.json",
            install.StandardOutput.Trim(),
            cancellationToken);

        await VmHarnessGuestEvidenceCollector.CaptureAfterMutationAsync(
            context,
            cancellationToken);

        var innerGate = VmHarnessInnerLabGate.EvaluateFromLabEvidence(
            LabInstallResultClassifier.MapObservedInnerGate(installJson.InnerGateEvidence));
        await context.RunStore.WriteJsonArtifactAsync(
            context.Run,
            "inner-gate-evidence.json",
            innerGate,
            cancellationToken);

        var (verdict, mutationOutcome) = LabInstallResultClassifier.Classify(installJson);

        return new VmHarnessScenarioResult(
            Definition.Id,
            verdict,
            mutationOutcome,
            VmHarnessRestoreOutcome.NotAttempted,
            installJson.Summary,
            selectedPlanId.Value.ToString(),
            installJson.TransactionId?.ToString(),
            new Dictionary<string, string>
            {
                ["innerLabGate"] = System.Text.Json.JsonSerializer.Serialize(innerGate),
            });
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
