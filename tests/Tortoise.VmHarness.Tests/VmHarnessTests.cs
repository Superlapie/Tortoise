using Tortoise.VmHarness.Domain;
using Tortoise.VmHarness.Evidence;
using Tortoise.VmHarness.Guest;
using Tortoise.VmHarness.Orchestration;
using Tortoise.VmHarness.Providers;
using Tortoise.VmHarness.Safety;
using Tortoise.VmHarness.Scenarios;
using Tortoise.VmHarness.Tests.Fakes;

namespace Tortoise.VmHarness.Tests;

public sealed class VmHarnessSafetyGateTests
{
    [Fact]
    public void EvaluateForMutation_BlocksWhenAnyConditionUnknown()
    {
        var result = VmHarnessSafetyGate.EvaluateForMutation(new VmHarnessSafetyEvaluationContext(
            TargetIsHyperVGuest: TriState.True,
            HostHyperVProofValid: TriState.True,
            CheckpointRecorded: TriState.True,
            GuestIsDisposableVm: TriState.Unknown));

        Assert.False(result.Allowed);
        Assert.NotEmpty(result.UnknownConditions);
    }

    [Fact]
    public void EvaluateForMutation_AllowsWhenAllConditionsTrue()
    {
        var result = VmHarnessSafetyGate.EvaluateForMutation(new VmHarnessSafetyEvaluationContext(
            TargetIsHyperVGuest: TriState.True,
            HostHyperVProofValid: TriState.True,
            CheckpointRecorded: TriState.True,
            GuestIsDisposableVm: TriState.True,
            LabMutationGatesSatisfied: TriState.True,
            PlanFrozen: TriState.True,
            UpdateLowRisk: TriState.True,
            UpdateWindowsRecommended: TriState.True,
            SourceIsWindowsUpdate: TriState.True,
            AuthoritativeHardwareIdMatch: TriState.True,
            BrowseOnlyKnownAndFalse: TriState.True,
            PackageDownloadedAndVerified: TriState.True,
            PendingRebootNotPending: TriState.True,
            BrokerToctouChecksPassed: TriState.True,
            ServicingLockAcquired: TriState.True,
            NoRestrictedDeviceCategory: TriState.True));

        Assert.True(result.Allowed);
        Assert.Equal(VmHarnessSafetyGate.RequiredConditionCount, result.SatisfiedConditions.Count);
    }
}

public sealed class VmHarnessDualVmProofTests
{
    [Fact]
    public void Evaluate_BlocksWhenGuestDisagrees()
    {
        var host = new VmHarnessHostVmProof("vm-id", "vm", "Running", null);
        var guest = new VmHarnessGuestEnvironmentProof(
            false,
            true,
            true,
            true,
            "Physical",
            "blocked",
            "raw");

        var result = VmHarnessDualVmProof.Evaluate(host, guest);
        Assert.False(result.ProofsAgree);
        Assert.NotNull(result.BlockReason);
    }
}

public sealed class VmHarnessRedactorTests
{
    [Fact]
    public void Redact_RemovesCapabilityTokens()
    {
        var redacted = VmHarnessRedactor.Redact("launch --capability=secret-token-value");
        Assert.DoesNotContain("secret-token-value", redacted, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", redacted, StringComparison.Ordinal);
    }
}

public sealed class TortoiseCliOutputParserTests
{
    [Fact]
    public void ParsePlanIds_FindsGuids()
    {
        var ids = TortoiseCliOutputParser.ParsePlanIds(
            "Plan aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee created.");
        Assert.Single(ids);
        Assert.Equal(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), ids[0]);
    }
}

public sealed class VmHarnessVerdictClassifierTests
{
    [Fact]
    public void ClassifyFinal_RestoreFailureOverridesPass()
    {
        var verdict = VmHarnessVerdictClassifier.ClassifyFinal(
            VmHarnessMutationOutcome.Succeeded,
            VmHarnessRestoreOutcome.Failed,
            new VmHarnessScenarioResult("s", VmHarnessVerdict.Pass, VmHarnessMutationOutcome.Succeeded, VmHarnessRestoreOutcome.Failed, "ok"),
            mutationAttempted: true);

        Assert.Equal(VmHarnessVerdict.RestoreFailed, verdict);
    }

    [Fact]
    public void ClassifyFinal_NoEligibleCandidatePreserved()
    {
        var verdict = VmHarnessVerdictClassifier.ClassifyFinal(
            VmHarnessMutationOutcome.NotAttempted,
            VmHarnessRestoreOutcome.Skipped,
            new VmHarnessScenarioResult("s", VmHarnessVerdict.NoEligibleCandidate, VmHarnessMutationOutcome.NotAttempted, VmHarnessRestoreOutcome.Skipped, "none"),
            mutationAttempted: false);

        Assert.Equal(VmHarnessVerdict.NoEligibleCandidate, verdict);
    }
}

public sealed class VmHarnessOrchestratorTests
{
    [Fact]
    public async Task DryRun_DoesNotCreateCheckpointOrMutate()
    {
        var provider = new FakeVmHarnessProvider();
        var store = new InMemoryRunStore();
        var guest = new FakeGuestTransport();
        var orchestrator = new VmHarnessOrchestrator(
            provider,
            store,
            guest,
            VmHarnessScenarioRegistry.CreateDefault());

        var result = await orchestrator.RunAsync(new VmHarnessRunOptions(
            "lab-vm",
            "baseline-low-risk-install",
            VmHarnessRunMode.DryRun,
            "artifacts/vm-harness",
            "abc123",
            TimeSpan.FromMinutes(1)));

        Assert.Equal(0, provider.CreateCheckpointCallCount);
        Assert.Equal(0, provider.RestoreCallCount);
        Assert.Equal(VmHarnessVerdict.DryRunComplete, result.Verdict);
        Assert.Equal(VmHarnessMutationOutcome.NotAttempted, result.MutationOutcome);
    }

    [Fact]
    public async Task ProveCheckpointRestore_CreatesAndRestoresWithoutMutation()
    {
        var provider = new FakeVmHarnessProvider();
        var store = new InMemoryRunStore();
        var guest = new FakeGuestTransport();
        var orchestrator = new VmHarnessOrchestrator(
            provider,
            store,
            guest,
            VmHarnessScenarioRegistry.CreateDefault());

        var result = await orchestrator.RunAsync(new VmHarnessRunOptions(
            "lab-vm",
            "baseline-low-risk-install",
            VmHarnessRunMode.ProveCheckpointRestore,
            "artifacts/vm-harness",
            "abc123",
            TimeSpan.FromMinutes(1)));

        Assert.Equal(1, provider.CreateCheckpointCallCount);
        Assert.Equal(1, provider.RestoreCallCount);
        Assert.Equal(VmHarnessVerdict.CheckpointRestoreProved, result.Verdict);
        Assert.Equal(VmHarnessMutationOutcome.NotAttempted, result.MutationOutcome);
        Assert.Equal(VmHarnessRestoreOutcome.Succeeded, result.RestoreOutcome);
    }

    [Fact]
    public async Task CheckpointCreationFailure_PreventsMutationExecution()
    {
        var provider = new FakeVmHarnessProvider { ShouldFailCheckpointCreation = true };
        var counting = new CountingScenario();
        var store = new InMemoryRunStore();
        var guest = new FakeGuestTransport();
        var orchestrator = new VmHarnessOrchestrator(
            provider,
            store,
            guest,
            new VmHarnessScenarioRegistry([counting]));

        var result = await orchestrator.RunAsync(new VmHarnessRunOptions(
            "lab-vm",
            "count",
            VmHarnessRunMode.ExecuteDisposableVmMutation,
            "artifacts/vm-harness",
            "abc123",
            TimeSpan.FromMinutes(1)));

        Assert.Equal(0, counting.ExecuteCount);
        Assert.Equal(VmHarnessVerdict.HarnessError, result.Verdict);
    }

    [Fact]
    public async Task RestoreFailure_ReportsRestoreFailedVerdict()
    {
        var provider = new FakeVmHarnessProvider { ShouldFailRestore = true };
        var store = new InMemoryRunStore();
        var guest = new FakeGuestTransport();
        var orchestrator = new VmHarnessOrchestrator(
            provider,
            store,
            guest,
            VmHarnessScenarioRegistry.CreateDefault());

        var result = await orchestrator.RunAsync(new VmHarnessRunOptions(
            "lab-vm",
            "baseline-low-risk-install",
            VmHarnessRunMode.ProveCheckpointRestore,
            "artifacts/vm-harness",
            "abc123",
            TimeSpan.FromMinutes(1)));

        Assert.Equal(VmHarnessVerdict.RestoreFailed, result.Verdict);
        Assert.Equal(VmHarnessRestoreOutcome.Failed, result.RestoreOutcome);
    }

    [Fact]
    public async Task GuestHostDisagreement_BlocksMutation()
    {
        var provider = new FakeVmHarnessProvider();
        var store = new InMemoryRunStore();
        var guest = new FakeGuestTransport { ReportDisposableVm = false };
        var counting = new CountingScenario();
        var orchestrator = new VmHarnessOrchestrator(
            provider,
            store,
            guest,
            new VmHarnessScenarioRegistry([counting]));

        var result = await orchestrator.RunAsync(new VmHarnessRunOptions(
            "lab-vm",
            "count",
            VmHarnessRunMode.ExecuteDisposableVmMutation,
            "artifacts/vm-harness",
            "abc123",
            TimeSpan.FromMinutes(1)));

        Assert.Equal(0, counting.ExecuteCount);
        Assert.Equal(VmHarnessVerdict.FailClosed, result.Verdict);
        Assert.Equal(VmHarnessMutationOutcome.Blocked, result.MutationOutcome);
        Assert.Equal(VmHarnessRestoreOutcome.Succeeded, result.RestoreOutcome);
    }

    [Fact]
    public async Task NoEligibleCandidate_IsSuccessfulHarnessOutcome()
    {
        var provider = new FakeVmHarnessProvider();
        var store = new InMemoryRunStore();
        var guest = new FakeGuestTransport { PlanOutput = "No actionable update plans were created." };
        var orchestrator = new VmHarnessOrchestrator(
            provider,
            store,
            guest,
            VmHarnessScenarioRegistry.CreateDefault());

        var result = await orchestrator.RunAsync(new VmHarnessRunOptions(
            "lab-vm",
            "baseline-low-risk-install",
            VmHarnessRunMode.DryRun,
            "artifacts/vm-harness",
            "abc123",
            TimeSpan.FromMinutes(1)));

        Assert.Equal(VmHarnessVerdict.NoEligibleCandidate, result.Verdict);
    }
}

public sealed class VmHarnessScenarioRegistryTests
{
    [Fact]
    public void CreateDefault_ContainsRequiredScenarios()
    {
        var ids = VmHarnessScenarioRegistry.CreateDefault().ListDefinitions().Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("baseline-low-risk-install", ids);
        Assert.Contains("abort-before-elevation", ids);
        Assert.Contains("wrong-pid-client", ids);
        Assert.Contains("post-install-inconclusive", ids);
    }
}

public sealed class HyperVGuestStatusParserTests
{
    [Fact]
    public void ParseStatus_ReadsDisposableVmFlag()
    {
        var proof = VmHarnessGuestStatusParser.ParseStatus(
            "Lab build: True\nMutation enabled: true\nEnvironment: DisposableVm\nReason: enabled\nDisposable VM detected: true");
        Assert.True(proof.IsDisposableVm);
        Assert.True(proof.MutationCapabilityEnabled);
    }
}

[Trait("Category", "HyperVIntegration")]
public sealed class HyperVIntegrationTests
{
    [Fact(Skip = "Opt-in Hyper-V integration test; requires local Hyper-V VM named TORTOISE_HARNESS_VM.")]
    public void Placeholder_OptInOnly()
    {
        Assert.True(OperatingSystem.IsWindows());
    }
}
