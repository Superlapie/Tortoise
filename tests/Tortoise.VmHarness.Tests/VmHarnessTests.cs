using System.IO.Compression;
using System.Runtime.Versioning;
using Tortoise.VmHarness.Domain;
using Tortoise.VmHarness.Evidence;
using Tortoise.VmHarness.Guest;
using Tortoise.VmHarness.Orchestration;
using Tortoise.VmHarness.Providers;
using Tortoise.VmHarness.Providers.HyperV;
using Tortoise.VmHarness.Safety;
using Tortoise.VmHarness.Scenarios;
using Tortoise.VmHarness.Tests.Fakes;

namespace Tortoise.VmHarness.Tests;

public sealed class VmHarnessHostPreGateTests
{
    [Fact]
    public void Evaluate_BlocksWhenGuestProofMissing()
    {
        var result = VmHarnessHostPreGate.Evaluate(new VmHarnessHostPreGateContext(
            TargetIsHyperVGuest: TriState.True,
            HostHyperVProofValid: TriState.True,
            CheckpointRecorded: TriState.True,
            GuestIsDisposableVm: TriState.Unknown));

        Assert.False(result.Allowed);
        Assert.NotEmpty(result.UnknownConditions);
    }

    [Fact]
    public void Evaluate_AllowsWhenHostPreGateConditionsAreKnownTrue()
    {
        var result = VmHarnessHostPreGate.Evaluate(new VmHarnessHostPreGateContext(
            TargetIsHyperVGuest: TriState.True,
            HostHyperVProofValid: TriState.True,
            CheckpointRecorded: TriState.True,
            GuestIsDisposableVm: TriState.True,
            LabMutationCapabilityEnabled: TriState.True));

        Assert.True(result.Allowed);
        Assert.Equal(VmHarnessHostPreGate.RequiredConditionCount, result.SatisfiedConditions.Count);
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

public sealed class VmHarnessPlanJsonParserTests
{
    [Fact]
    public void SelectEligibleBaselinePlan_ReturnsLowRiskWindowsRecommendedPlan()
    {
        var planId = VmHarnessPlanJsonParser.SelectEligibleBaselinePlan(
            "{\"plans\":[{\"planId\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\",\"riskLevel\":\"Low\",\"classification\":\"WindowsRecommended\"}]}");
        Assert.Equal(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), planId);
    }

    [Fact]
    public void SelectEligibleBaselinePlan_RejectsIneligibleFirstPlan()
    {
        var planId = VmHarnessPlanJsonParser.SelectEligibleBaselinePlan(
            "{\"plans\":[{\"planId\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\",\"riskLevel\":\"High\",\"classification\":\"ReviewRequired\"}]}");
        Assert.Null(planId);
    }
}

public sealed class LabInstallResultClassifierTests
{
    [Fact]
    public void Classify_AwaitingReboot_IsRecoveryRequiredNotBlocked()
    {
        var (verdict, outcome) = LabInstallResultClassifier.Classify(new HarnessLabVmInstallJson(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "AwaitingReboot",
            false,
            "InstalledRestartRequired",
            true,
            true,
            true,
            0,
            null,
            "AwaitingReboot",
            "reboot required",
            new HarnessLabObservedInnerGateJson(
                PreInstallPendingRebootNotPending: "True")));

        Assert.Equal(VmHarnessVerdict.RecoveryRequired, verdict);
        Assert.Equal(VmHarnessMutationOutcome.Inconclusive, outcome);
    }

    [Fact]
    public void MapObservedInnerGate_DoesNotInferTrueFromMissingEvidence()
    {
        var evidence = LabInstallResultClassifier.MapObservedInnerGate(null);
        Assert.Equal(TriState.Unknown, evidence.PlanFrozen);
        Assert.Equal(TriState.Unknown, evidence.BrokerToctouChecksPassed);
    }
}

public sealed class VmHarnessRemoteCommandParserTests
{
    [Fact]
    public void ParseStatusJson_ReadsExplicitMarkerFields()
    {
        var status = HarnessLabJsonParser.ParseStatusJson(
            "{\"isLabBuild\":true,\"isDisposableVm\":true,\"mutationTestsEnabled\":true,\"vmInstallExplicitlyAllowed\":true,\"mutationCapabilityEnabled\":true,\"mutationEnvironment\":\"DisposableVm\",\"reason\":\"enabled\"}");
        Assert.True(status.MutationTestsEnabled);
        Assert.True(status.VmInstallExplicitlyAllowed);
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
        Assert.True(result.RestoreResult?.GuestReachable);
    }

    [Fact]
    public async Task ExecuteMutation_AllowsWhenHostPreGatePasses()
    {
        var provider = new FakeVmHarnessProvider();
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

        Assert.Equal(1, counting.ExecuteCount);
        Assert.Equal(VmHarnessVerdict.Pass, result.Verdict);
    }

    [Fact]
    public async Task ThrowingScenarioAfterCheckpoint_StillAttemptsRestore()
    {
        var provider = new FakeVmHarnessProvider();
        var store = new InMemoryRunStore();
        var guest = new FakeGuestTransport();
        var orchestrator = new VmHarnessOrchestrator(
            provider,
            store,
            guest,
            new VmHarnessScenarioRegistry([new ThrowingScenario()]));

        var result = await orchestrator.RunAsync(new VmHarnessRunOptions(
            "lab-vm",
            "throws",
            VmHarnessRunMode.ExecuteDisposableVmMutation,
            "artifacts/vm-harness",
            "abc123",
            TimeSpan.FromMinutes(1)));

        Assert.Equal(1, provider.RestoreCallCount);
        Assert.Equal(VmHarnessVerdict.HarnessError, result.Verdict);
    }

    [Fact]
    public async Task GuestProofThrowsAfterCheckpoint_StillAttemptsRestore()
    {
        var provider = new FakeVmHarnessProvider();
        var store = new InMemoryRunStore();
        var guest = new FakeGuestTransport { ThrowOnStatus = true };
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

        Assert.Equal(1, provider.RestoreCallCount);
        Assert.Equal(VmHarnessRestoreOutcome.Succeeded, result.RestoreOutcome);
        Assert.Equal(0, counting.ExecuteCount);
        Assert.Equal(VmHarnessVerdict.HarnessError, result.Verdict);
    }

    [Fact]
    public async Task GuestHostDisagreement_BlocksMutationButRestores()
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
        Assert.Equal(1, provider.RestoreCallCount);
    }

    [Fact]
    public async Task NoEligibleCandidate_IsSuccessfulHarnessOutcome()
    {
        var provider = new FakeVmHarnessProvider();
        var store = new InMemoryRunStore();
        var guest = new FakeGuestTransport { PlanJson = "{\"plans\":[]}" };
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

    [Fact]
    public async Task IneligiblePlanOnly_ReturnsNoEligibleCandidate()
    {
        var provider = new FakeVmHarnessProvider();
        var store = new InMemoryRunStore();
        var guest = new FakeGuestTransport
        {
            PlanJson = "{\"plans\":[{\"planId\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\",\"riskLevel\":\"High\",\"classification\":\"ReviewRequired\"}]}",
        };
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

[Trait("Category", "HyperVIntegration")]
[SupportedOSPlatform("windows")]
public sealed class HyperVIntegrationTests
{
    [Fact]
    public async Task OptIn_ResolveCreateRestoreLifecycle()
    {
        if (!HyperVIntegrationEnvironment.TryGetConfiguration(out var vmName))
        {
            return;
        }

        await RunHyperVLifecycleAsync(vmName);
    }

    [SupportedOSPlatform("windows")]
    private static async Task RunHyperVLifecycleAsync(string vmName)
    {
        var provider = new HyperVVmHarnessProvider();
        await provider.EnsureHostRequirementsAsync();
        var target = await provider.ResolveVmAsync(vmName);
        var boundTarget = await provider.VerifyTargetIdentityAsync(target);
        Assert.Equal(target.HyperVVmId, boundTarget.HyperVVmId);

        var checkpoint = await provider.CreateCheckpointAsync(
            boundTarget,
            $"TortoiseHarness-test-{Guid.NewGuid():N}");
        Assert.Equal(boundTarget.HyperVVmId, checkpoint.HyperVVmId);

        var restore = await provider.RestoreCheckpointAsync(boundTarget, checkpoint);
        Assert.True(restore.HyperVStateRunning);

        if (HyperVIntegrationEnvironment.TryGetGuestTransport(out var guestTransport))
        {
            var guestTarget = VmHarnessGuestTargetFactory.FromVmTarget(boundTarget);
            var reachable = await guestTransport.ProbeReachabilityAsync(
                guestTarget,
                TimeSpan.FromMinutes(2));
            Assert.True(reachable);
        }
    }
}

internal static class HyperVIntegrationEnvironment
{
    [SupportedOSPlatform("windows")]
    public static bool TryGetConfiguration(out string vmName)
    {
        vmName = string.Empty;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var configuredVmName = Environment.GetEnvironmentVariable("TORTOISE_HARNESS_VM");
        if (string.IsNullOrWhiteSpace(configuredVmName))
        {
            return false;
        }

        vmName = configuredVmName;
        return true;
    }

    [SupportedOSPlatform("windows")]
    public static bool TryGetGuestTransport(out IVmHarnessGuestTransport guestTransport)
    {
        guestTransport = new HyperVPowerShellDirectGuestTransport();
        return guestTransport.IsConfigured;
    }
}
