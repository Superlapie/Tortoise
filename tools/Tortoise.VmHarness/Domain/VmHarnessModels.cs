namespace Tortoise.VmHarness.Domain;

public static class VmHarnessConstants
{
    public const string HarnessVersion = "0.1.0-alpha-batch16";
    public const string DefaultArtifactsRoot = "artifacts/vm-harness";
    public const string ExecuteMutationFlag = "--execute-disposable-vm-mutation";
    public const string ProveCheckpointRestoreFlag = "--prove-checkpoint-restore";
}

public enum VmHarnessVerdict
{
    Pass = 0,
    FailClosed = 1,
    NoEligibleCandidate = 2,
    RecoveryRequired = 3,
    RestoreFailed = 4,
    HarnessError = 5,
    DryRunComplete = 6,
    CheckpointRestoreProved = 7,
}

public enum VmHarnessMutationOutcome
{
    NotAttempted = 0,
    Succeeded = 1,
    Failed = 2,
    Inconclusive = 3,
    Blocked = 4,
}

public enum VmHarnessRestoreOutcome
{
    NotAttempted = 0,
    Succeeded = 1,
    Failed = 2,
    Skipped = 3,
}

public enum VmHarnessRunMode
{
    Inspect = 0,
    DryRun = 1,
    ProveCheckpointRestore = 2,
    ExecuteDisposableVmMutation = 3,
}

public sealed record VmHarnessRun(
    string RunId,
    string VmName,
    string ScenarioId,
    VmHarnessRunMode Mode,
    DateTimeOffset StartedAtUtc,
    string? TortoiseCommitSha,
    string HarnessVersion,
    string? HyperVVmId = null,
    string? CheckpointName = null,
    string? CheckpointId = null,
    DateTimeOffset? CheckpointCreatedAtUtc = null);

public sealed record VmHarnessCheckpoint(
    string Name,
    string? Id,
    DateTimeOffset CreatedAtUtc,
    string HyperVVmId,
    string VmName);

public sealed record VmHarnessVmTarget(
    string Name,
    string HyperVVmId,
    string State,
    bool CheckpointOperationsAvailable);

public sealed record VmHarnessGuestEnvironmentProof(
    bool IsDisposableVm,
    bool MutationTestsEnabled,
    bool AllowVmInstallEnabled,
    bool MutationCapabilityEnabled,
    string Environment,
    string CapabilityReason,
    string RawStatusOutput);

public sealed record VmHarnessHostVmProof(
    string HyperVVmId,
    string VmName,
    string VmState,
    VmHarnessCheckpoint? Checkpoint);

public sealed record VmHarnessDualVmProofResult(
    bool HostProofValid,
    bool GuestProofValid,
    bool ProofsAgree,
    VmHarnessHostVmProof? HostProof,
    VmHarnessGuestEnvironmentProof? GuestProof,
    string? BlockReason);

public sealed record VmHarnessScenarioDefinition(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<string> Prerequisites,
    string InjectionPoint,
    IReadOnlyList<string> ExpectedTransactionStates,
    string ExpectedBrokerResponse,
    string ExpectedMutationStatus,
    string ExpectedRecoveryState,
    IReadOnlyList<string> EvidenceCaptured,
    bool RequiresRealMutation);

public sealed record VmHarnessScenarioResult(
    string ScenarioId,
    VmHarnessVerdict Verdict,
    VmHarnessMutationOutcome MutationOutcome,
    VmHarnessRestoreOutcome RestoreOutcome,
    string Summary,
    string? PlanId = null,
    string? TransactionId = null,
    IReadOnlyDictionary<string, string>? AdditionalEvidence = null);

public sealed record VmHarnessEvidenceManifest(
    string RunId,
    string ScenarioId,
    VmHarnessRunMode Mode,
    VmHarnessVerdict Verdict,
    VmHarnessMutationOutcome MutationOutcome,
    VmHarnessRestoreOutcome RestoreOutcome,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<string> ArtifactFiles);

public sealed record VmHarnessRunOptions(
    string VmName,
    string ScenarioId,
    VmHarnessRunMode Mode,
    string ArtifactsRoot,
    string? TortoiseCommitSha,
    TimeSpan CommandTimeout,
    string? LabDatabasePath = null);

public sealed record VmHarnessSafetyGateResult(
    bool Allowed,
    IReadOnlyList<string> SatisfiedConditions,
    IReadOnlyList<string> FailedConditions,
    IReadOnlyList<string> UnknownConditions);

public sealed record VmHarnessRestoreResult(
    bool Succeeded,
    string CheckpointName,
    string? CheckpointId,
    bool HyperVStateRunning,
    bool GuestReachable,
    string Summary,
    string? Error = null);

public sealed record VmHarnessOrchestrationResult(
    VmHarnessRun Run,
    VmHarnessVerdict Verdict,
    VmHarnessMutationOutcome MutationOutcome,
    VmHarnessRestoreOutcome RestoreOutcome,
    VmHarnessScenarioResult? ScenarioResult,
    VmHarnessRestoreResult? RestoreResult,
    string ReportPath,
    string ArtifactsDirectory);
