using Tortoise.VmHarness.Domain;
using Tortoise.VmHarness.Evidence;
using Tortoise.VmHarness.Guest;
using Tortoise.VmHarness.Providers;
using Tortoise.VmHarness.Reporting;
using Tortoise.VmHarness.Safety;
using Tortoise.VmHarness.Scenarios;
using Tortoise.VmHarness.Storage;

namespace Tortoise.VmHarness.Orchestration;

public sealed class VmHarnessOrchestrator
{
    private static readonly TimeSpan RestoreCleanupTimeout = TimeSpan.FromMinutes(10);

    private readonly IVmHarnessProvider _provider;
    private readonly IVmHarnessRunStore _runStore;
    private readonly IVmHarnessGuestTransport _guestTransport;
    private readonly VmHarnessScenarioRegistry _scenarioRegistry;

    public VmHarnessOrchestrator(
        IVmHarnessProvider provider,
        IVmHarnessRunStore runStore,
        IVmHarnessGuestTransport guestTransport,
        VmHarnessScenarioRegistry scenarioRegistry)
    {
        _provider = provider;
        _runStore = runStore;
        _guestTransport = guestTransport;
        _scenarioRegistry = scenarioRegistry;
    }

    public async Task<VmHarnessOrchestrationResult> InspectAsync(
        string vmName,
        CancellationToken cancellationToken = default)
    {
        await _provider.EnsureHostRequirementsAsync(cancellationToken);
        var target = await _provider.ResolveVmAsync(vmName, cancellationToken);
        var options = new VmHarnessRunOptions(
            vmName,
            "inspect",
            VmHarnessRunMode.Inspect,
            VmHarnessConstants.DefaultArtifactsRoot,
            VmHarnessGitMetadata.TryResolveCommitSha(),
            TimeSpan.FromMinutes(5));
        var run = await _runStore.CreateRunAsync(options, cancellationToken);
        run = run with { HyperVVmId = target.HyperVVmId };
        await _runStore.WriteJsonArtifactAsync(run, "run.json", run, cancellationToken);
        await _runStore.WriteJsonArtifactAsync(run, "preflight.json", target, cancellationToken);

        var reportPath = Path.Combine(_runStore.GetRunDirectory(run), "REPORT.md");
        await _runStore.WriteTextArtifactAsync(
            run,
            "REPORT.md",
            $"# Inspect\n\nVM `{target.Name}` resolved.\nHyper-V ID: `{target.HyperVVmId}`\nState: `{target.State}`\n",
            cancellationToken);

        return new VmHarnessOrchestrationResult(
            run,
            VmHarnessVerdict.DryRunComplete,
            VmHarnessMutationOutcome.NotAttempted,
            VmHarnessRestoreOutcome.Skipped,
            null,
            null,
            reportPath,
            _runStore.GetRunDirectory(run));
    }

    public Task<VmHarnessOrchestrationResult> RunAsync(
        VmHarnessRunOptions options,
        CancellationToken cancellationToken = default) =>
        RunInternalAsync(options, cancellationToken);

    private async Task<VmHarnessOrchestrationResult> RunInternalAsync(
        VmHarnessRunOptions options,
        CancellationToken cancellationToken)
    {
        var scenario = _scenarioRegistry.GetRequired(options.ScenarioId);
        var executeMutation = options.Mode == VmHarnessRunMode.ExecuteDisposableVmMutation;
        var proveCheckpoint = options.Mode == VmHarnessRunMode.ProveCheckpointRestore
            || executeMutation;

        await _provider.EnsureHostRequirementsAsync(cancellationToken);
        var target = await _provider.ResolveVmAsync(options.VmName, cancellationToken);
        var run = await _runStore.CreateRunAsync(options, cancellationToken);
        var labDatabasePath = VmHarnessLabDatabaseResolver.ResolveForRun(run.RunId);
        run = run with { HyperVVmId = target.HyperVVmId };

        await _runStore.WriteJsonArtifactAsync(run, "run.json", run, cancellationToken);
        await _runStore.WriteJsonArtifactAsync(run, "preflight.json", target, cancellationToken);
        await _runStore.WriteJsonArtifactAsync(
            run,
            "scenario.json",
            scenario.Definition,
            cancellationToken);

        VmHarnessCheckpoint? checkpoint = null;
        var checkpointCreated = false;
        VmHarnessRestoreResult? restoreResult = null;
        VmHarnessDualVmProofResult? dualProof = null;
        VmHarnessSafetyGateResult? hostPreGate = null;
        VmHarnessSafetyGateResult? innerLabGate = null;
        VmHarnessScenarioResult? scenarioResult = null;
        var mutationOutcome = VmHarnessMutationOutcome.NotAttempted;
        var restoreOutcome = VmHarnessRestoreOutcome.Skipped;

        try
        {
            if (proveCheckpoint)
            {
                target = await _provider.VerifyTargetIdentityAsync(target, cancellationToken);
                var checkpointName = VmHarnessCheckpointNaming.CreateCheckpointName(run.RunId, DateTimeOffset.UtcNow);
                checkpoint = await _provider.CreateCheckpointAsync(target, checkpointName, cancellationToken);
                checkpointCreated = true;
                run = run with
                {
                    CheckpointName = checkpoint.Name,
                    CheckpointId = checkpoint.Id,
                    CheckpointCreatedAtUtc = checkpoint.CreatedAtUtc,
                };
                await _runStore.WriteJsonArtifactAsync(run, "checkpoint.json", checkpoint, cancellationToken);

                var hostProof = new VmHarnessHostVmProof(
                    target.HyperVVmId,
                    target.Name,
                    target.State,
                    checkpoint);
                VmHarnessGuestEnvironmentProof? guestProof = null;
                if (_guestTransport.IsConfigured)
                {
                    guestProof = await _guestTransport.GetEnvironmentProofAsync(target.Name, cancellationToken);
                    await _runStore.WriteJsonArtifactAsync(run, "guest-environment.json", guestProof, cancellationToken);
                }

                dualProof = VmHarnessDualVmProof.Evaluate(hostProof, guestProof);
                await _runStore.WriteJsonArtifactAsync(run, "verification.json", dualProof, cancellationToken);

                hostPreGate = VmHarnessHostPreGate.Evaluate(
                    new VmHarnessHostPreGateContext(
                        TargetIsHyperVGuest: TriState.True,
                        HostHyperVProofValid: TriState.True,
                        CheckpointRecorded: TriState.True,
                        GuestIsDisposableVm: guestProof is null
                            ? TriState.Unknown
                            : guestProof.IsDisposableVm ? TriState.True : TriState.False,
                        LabMutationCapabilityEnabled: guestProof?.MutationCapabilityEnabled == true
                            ? TriState.True
                            : guestProof is null ? TriState.Unknown : TriState.False));

                await _runStore.WriteJsonArtifactAsync(run, "preflight.json", hostPreGate, cancellationToken);

                if (executeMutation && !dualProof.ProofsAgree)
                {
                    mutationOutcome = VmHarnessMutationOutcome.Blocked;
                    scenarioResult = new VmHarnessScenarioResult(
                        options.ScenarioId,
                        VmHarnessVerdict.FailClosed,
                        mutationOutcome,
                        VmHarnessRestoreOutcome.NotAttempted,
                        dualProof.BlockReason ?? "Host/guest VM proof failed.");
                }
                else if (executeMutation && !hostPreGate.Allowed)
                {
                    mutationOutcome = VmHarnessMutationOutcome.Blocked;
                    scenarioResult = new VmHarnessScenarioResult(
                        options.ScenarioId,
                        VmHarnessVerdict.FailClosed,
                        mutationOutcome,
                        VmHarnessRestoreOutcome.NotAttempted,
                        "Host pre-gate blocked mutation.");
                }
                else if (executeMutation)
                {
                    var context = new VmHarnessScenarioContext
                    {
                        Run = run,
                        Options = options with { LabDatabasePath = labDatabasePath },
                        Target = target,
                        Checkpoint = checkpoint,
                        GuestProof = guestProof,
                        LabDatabasePath = labDatabasePath,
                        GuestTransport = _guestTransport,
                        RunStore = _runStore,
                        ExecuteMutation = true,
                        CancellationToken = cancellationToken,
                    };
                    scenarioResult = await scenario.ExecuteAsync(context, cancellationToken);
                    mutationOutcome = scenarioResult.MutationOutcome;

                    if (scenarioResult.AdditionalEvidence?.TryGetValue("innerLabGate", out var innerJson) == true
                        && !string.IsNullOrWhiteSpace(innerJson))
                    {
                        innerLabGate = System.Text.Json.JsonSerializer.Deserialize<VmHarnessSafetyGateResult>(innerJson);
                    }
                }
                else
                {
                    scenarioResult = new VmHarnessScenarioResult(
                        options.ScenarioId,
                        VmHarnessVerdict.CheckpointRestoreProved,
                        VmHarnessMutationOutcome.NotAttempted,
                        VmHarnessRestoreOutcome.NotAttempted,
                        "Checkpoint created; mutation not requested.");
                }
            }
            else
            {
                var context = new VmHarnessScenarioContext
                {
                    Run = run,
                    Options = options with { LabDatabasePath = labDatabasePath },
                    Target = target,
                    LabDatabasePath = labDatabasePath,
                    GuestTransport = _guestTransport,
                    RunStore = _runStore,
                    ExecuteMutation = false,
                    CancellationToken = cancellationToken,
                };
                scenarioResult = await scenario.ExecuteAsync(context, cancellationToken);
                mutationOutcome = scenarioResult.MutationOutcome;
            }
        }
        catch (Exception ex)
        {
            scenarioResult ??= new VmHarnessScenarioResult(
                options.ScenarioId,
                VmHarnessVerdict.HarnessError,
                VmHarnessMutationOutcome.NotAttempted,
                restoreOutcome,
                ex.Message);
        }
        finally
        {
            if (checkpointCreated && checkpoint is not null)
            {
                using var cleanupCts = new CancellationTokenSource(RestoreCleanupTimeout);
                try
                {
                    target = await _provider.VerifyTargetIdentityAsync(target, cleanupCts.Token);
                    restoreResult = await _provider.RestoreCheckpointAsync(target, checkpoint, cleanupCts.Token);
                    restoreOutcome = restoreResult.Succeeded
                        ? VmHarnessRestoreOutcome.Succeeded
                        : VmHarnessRestoreOutcome.Failed;

                    if (restoreResult.HyperVStateRunning)
                    {
                        _ = await _provider.WaitForHyperVRunningAsync(target, options.CommandTimeout, cleanupCts.Token);
                    }

                    if (_guestTransport.IsConfigured)
                    {
                        var guestReachable = await _guestTransport.ProbeReachabilityAsync(
                            target.Name,
                            options.CommandTimeout,
                            cleanupCts.Token);
                        restoreResult = restoreResult with { GuestReachable = guestReachable };
                    }

                    await _runStore.WriteJsonArtifactAsync(run, "restore-result.json", restoreResult, cleanupCts.Token);
                }
                catch (Exception restoreEx)
                {
                    restoreOutcome = VmHarnessRestoreOutcome.Failed;
                    restoreResult = new VmHarnessRestoreResult(
                        false,
                        checkpoint.Name,
                        checkpoint.Id,
                        false,
                        false,
                        "Checkpoint restore failed during cleanup.",
                        restoreEx.Message);
                    try
                    {
                        await _runStore.WriteJsonArtifactAsync(run, "restore-result.json", restoreResult, CancellationToken.None);
                    }
                    catch
                    {
                        // Best effort only during cleanup failure.
                    }
                }
            }
        }

        if (innerLabGate is not null)
        {
            await _runStore.WriteJsonArtifactAsync(run, "broker-result.json", innerLabGate, cancellationToken);
        }
        else if (hostPreGate is not null)
        {
            await _runStore.WriteJsonArtifactAsync(run, "broker-result.json", hostPreGate, cancellationToken);
        }

        var verdict = VmHarnessVerdictClassifier.ClassifyFinal(
            mutationOutcome,
            restoreOutcome,
            scenarioResult,
            executeMutation);

        if (restoreOutcome == VmHarnessRestoreOutcome.Failed)
        {
            verdict = VmHarnessVerdict.RestoreFailed;
        }

        var report = VmHarnessReportWriter.BuildReport(
            run,
            verdict,
            mutationOutcome,
            restoreOutcome,
            scenarioResult,
            restoreResult,
            dualProof,
            hostPreGate);
        await _runStore.WriteTextArtifactAsync(run, "REPORT.md", report, cancellationToken);

        var manifest = new VmHarnessEvidenceManifest(
            run.RunId,
            run.ScenarioId,
            run.Mode,
            verdict,
            mutationOutcome,
            restoreOutcome,
            DateTimeOffset.UtcNow,
            Directory.Exists(_runStore.GetRunDirectory(run))
                ? Directory.GetFiles(_runStore.GetRunDirectory(run)).Select(Path.GetFileName).Where(n => n is not null).Cast<string>().OrderBy(n => n).ToList()
                : []);

        await _runStore.FinalizeManifestAsync(run, manifest, cancellationToken);

        return new VmHarnessOrchestrationResult(
            run,
            verdict,
            mutationOutcome,
            restoreOutcome,
            scenarioResult,
            restoreResult,
            Path.Combine(_runStore.GetRunDirectory(run), "REPORT.md"),
            _runStore.GetRunDirectory(run));
    }
}
