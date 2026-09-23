using Tortoise.VmHarness.Domain;
using Tortoise.VmHarness.Guest;
using Tortoise.VmHarness.Providers;
using Tortoise.VmHarness.Scenarios;
using Tortoise.VmHarness.Storage;

namespace Tortoise.VmHarness.Tests.Fakes;

internal sealed class FakeVmHarnessProvider : IVmHarnessProvider
{
    public string ProviderName => "Fake";
    public bool ShouldFailCheckpointCreation { get; set; }
    public bool ShouldFailRestore { get; set; }
    public int ResolveCallCount { get; private set; }
    public int CreateCheckpointCallCount { get; private set; }
    public int RestoreCallCount { get; private set; }
    public IReadOnlyList<string> ResolvedNames { get; private set; } = [];

    public Task EnsureHostRequirementsAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<VmHarnessVmTarget> ResolveVmAsync(string vmName, CancellationToken cancellationToken = default)
    {
        ResolveCallCount++;
        ResolvedNames = ResolvedNames.Append(vmName).ToList();

        if (string.Equals(vmName, "missing", StringComparison.OrdinalIgnoreCase))
        {
            throw new VmHarnessProviderException("VM not found.");
        }

        if (string.Equals(vmName, "ambiguous", StringComparison.OrdinalIgnoreCase))
        {
            throw new VmHarnessProviderException("Ambiguous Hyper-V VM name.");
        }

        return Task.FromResult(new VmHarnessVmTarget(vmName, Guid.NewGuid().ToString(), "Running", true));
    }

    public Task<VmHarnessCheckpoint> CreateCheckpointAsync(
        VmHarnessVmTarget target,
        string checkpointName,
        CancellationToken cancellationToken = default)
    {
        CreateCheckpointCallCount++;
        if (ShouldFailCheckpointCreation)
        {
            throw new VmHarnessProviderException("Checkpoint creation failed.");
        }

        return Task.FromResult(new VmHarnessCheckpoint(
            checkpointName,
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow,
            target.HyperVVmId,
            target.Name));
    }

    public Task<VmHarnessRestoreResult> RestoreCheckpointAsync(
        VmHarnessVmTarget target,
        VmHarnessCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        RestoreCallCount++;
        if (ShouldFailRestore)
        {
            return Task.FromResult(new VmHarnessRestoreResult(
                false,
                checkpoint.Name,
                checkpoint.Id,
                false,
                "Restore failed.",
                "simulated failure"));
        }

        return Task.FromResult(new VmHarnessRestoreResult(
            true,
            checkpoint.Name,
            checkpoint.Id,
            true,
            "Restored."));
    }

    public Task WaitForVmRunningAsync(
        VmHarnessVmTarget target,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

internal sealed class FakeGuestTransport : IVmHarnessGuestTransport
{
    public bool IsConfigured { get; init; } = true;
    public bool ReportDisposableVm { get; init; } = true;
    public bool ReportMutationEnabled { get; init; } = true;
    public string PlanOutput { get; init; } =
        "Plan 11111111-1111-1111-1111-111111111111\nRisk: Low\nClassification: WindowsRecommended";

    public Task<VmHarnessGuestEnvironmentProof> GetEnvironmentProofAsync(
        string vmName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new VmHarnessGuestEnvironmentProof(
            ReportDisposableVm,
            true,
            true,
            ReportMutationEnabled,
            "DisposableVm",
            "enabled",
            "Disposable VM detected: true\nMutation enabled: true"));

    public Task<VmHarnessGuestCommandResult> ExecuteCommandAsync(
        string vmName,
        string command,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        if (command.StartsWith("tortoise scan", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new VmHarnessGuestCommandResult(0, "scan ok", string.Empty, TimeSpan.FromSeconds(1)));
        }

        if (command.StartsWith("tortoise plan", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new VmHarnessGuestCommandResult(0, PlanOutput, string.Empty, TimeSpan.FromSeconds(1)));
        }

        if (command.StartsWith("tortoise preflight", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new VmHarnessGuestCommandResult(0, "preflight ok", string.Empty, TimeSpan.FromSeconds(1)));
        }

        if (command.StartsWith("tortoise-lab vm install", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new VmHarnessGuestCommandResult(0, "install ok", string.Empty, TimeSpan.FromSeconds(1)));
        }

        return Task.FromResult(new VmHarnessGuestCommandResult(0, command, string.Empty, TimeSpan.Zero));
    }
}

internal sealed class InMemoryRunStore : IVmHarnessRunStore
{
    private readonly Dictionary<string, Dictionary<string, string>> _files = new();

    public Task<VmHarnessRun> CreateRunAsync(VmHarnessRunOptions options, CancellationToken cancellationToken = default)
    {
        var run = new VmHarnessRun(
            Guid.NewGuid().ToString("N"),
            options.VmName,
            options.ScenarioId,
            options.Mode,
            DateTimeOffset.UtcNow,
            options.TortoiseCommitSha,
            VmHarnessConstants.HarnessVersion);
        _files[run.RunId] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(run);
    }

    public string GetRunDirectory(VmHarnessRun run) => Path.Combine("artifacts", "vm-harness", run.RunId);

    public Task WriteJsonArtifactAsync<T>(VmHarnessRun run, string fileName, T payload, CancellationToken cancellationToken = default) =>
        WriteTextArtifactAsync(run, fileName, System.Text.Json.JsonSerializer.Serialize(payload), cancellationToken);

    public Task WriteTextArtifactAsync(VmHarnessRun run, string fileName, string content, CancellationToken cancellationToken = default)
    {
        _files[run.RunId][fileName] = content;
        return Task.CompletedTask;
    }

    public Task WriteSanitizedLogAsync(VmHarnessRun run, string fileName, string rawLog, CancellationToken cancellationToken = default) =>
        WriteTextArtifactAsync(run, fileName, rawLog, cancellationToken);

    public Task<VmHarnessEvidenceManifest> FinalizeManifestAsync(
        VmHarnessRun run,
        VmHarnessEvidenceManifest manifest,
        CancellationToken cancellationToken = default) =>
        WriteJsonArtifactAsync(run, "verification.json", manifest, cancellationToken).ContinueWith(_ => manifest, cancellationToken);
}

internal sealed class ThrowingScenario : IVmHarnessScenario
{
    public VmHarnessScenarioDefinition Definition { get; } = new(
        "throws",
        "Throws",
        "test",
        [],
        "none",
        [],
        "none",
        "none",
        "none",
        [],
        false);

    public Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("scenario failed");
}

internal sealed class CountingScenario : IVmHarnessScenario
{
    public int ExecuteCount { get; private set; }

    public VmHarnessScenarioDefinition Definition { get; } = new(
        "count",
        "Count",
        "test",
        [],
        "none",
        [],
        "none",
        "none",
        "none",
        [],
        true);

    public Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default)
    {
        ExecuteCount++;
        return Task.FromResult(new VmHarnessScenarioResult(
            Definition.Id,
            context.ExecuteMutation ? VmHarnessVerdict.Pass : VmHarnessVerdict.DryRunComplete,
            context.ExecuteMutation ? VmHarnessMutationOutcome.Succeeded : VmHarnessMutationOutcome.NotAttempted,
            VmHarnessRestoreOutcome.NotAttempted,
            "ok"));
    }
}
