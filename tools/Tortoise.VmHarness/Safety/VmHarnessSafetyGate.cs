using Tortoise.VmHarness.Domain;

namespace Tortoise.VmHarness.Safety;

public static class VmHarnessSafetyGate
{
    public const int RequiredConditionCount = 16;

    private static readonly string[] ConditionNames =
    [
        "Target is a positively identified Hyper-V guest VM",
        "Host harness identifies the VM through the hypervisor",
        "A dedicated checkpoint exists with a recorded identifier for this run",
        "Guest disposable-VM detector reports IsDisposableVm=true",
        "Lab mutation environment gates are satisfied",
        "Target plan is frozen",
        "Update risk is low",
        "Update is Windows Recommended",
        "Update source is Windows Update",
        "Candidate has authoritative hardware-ID match",
        "BrowseOnly is known and false",
        "Package is downloaded and verified before elevation",
        "Pending reboot state is known and NotPending",
        "Broker final live TOCTOU checks passed",
        "Servicing lock was acquired for install",
        "No restricted device category is involved",
    ];

    public static IReadOnlyList<string> ConditionDescriptions => ConditionNames;

    public static VmHarnessSafetyGateResult EvaluateForMutation(VmHarnessSafetyEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var satisfied = new List<string>();
        var failed = new List<string>();
        var unknown = new List<string>();

        Evaluate(0, context.TargetIsHyperVGuest, satisfied, failed, unknown);
        Evaluate(1, context.HostHyperVProofValid, satisfied, failed, unknown);
        Evaluate(2, context.CheckpointRecorded, satisfied, failed, unknown);
        Evaluate(3, context.GuestIsDisposableVm, satisfied, failed, unknown);
        Evaluate(4, context.LabMutationGatesSatisfied, satisfied, failed, unknown);
        Evaluate(5, context.PlanFrozen, satisfied, failed, unknown);
        Evaluate(6, context.UpdateLowRisk, satisfied, failed, unknown);
        Evaluate(7, context.UpdateWindowsRecommended, satisfied, failed, unknown);
        Evaluate(8, context.SourceIsWindowsUpdate, satisfied, failed, unknown);
        Evaluate(9, context.AuthoritativeHardwareIdMatch, satisfied, failed, unknown);
        Evaluate(10, context.BrowseOnlyKnownAndFalse, satisfied, failed, unknown);
        Evaluate(11, context.PackageDownloadedAndVerified, satisfied, failed, unknown);
        Evaluate(12, context.PendingRebootNotPending, satisfied, failed, unknown);
        Evaluate(13, context.BrokerToctouChecksPassed, satisfied, failed, unknown);
        Evaluate(14, context.ServicingLockAcquired, satisfied, failed, unknown);
        Evaluate(15, context.NoRestrictedDeviceCategory, satisfied, failed, unknown);

        var allowed = failed.Count == 0 && unknown.Count == 0;
        return new VmHarnessSafetyGateResult(allowed, satisfied, failed, unknown);
    }

    private static void Evaluate(
        int index,
        TriState state,
        ICollection<string> satisfied,
        ICollection<string> failed,
        ICollection<string> unknown)
    {
        var label = ConditionNames[index];
        switch (state)
        {
            case TriState.True:
                satisfied.Add(label);
                break;
            case TriState.False:
                failed.Add(label);
                break;
            default:
                unknown.Add(label);
                break;
        }
    }
}

public enum TriState
{
    Unknown = 0,
    False = 1,
    True = 2,
}

public sealed record VmHarnessSafetyEvaluationContext(
    TriState TargetIsHyperVGuest = TriState.Unknown,
    TriState HostHyperVProofValid = TriState.Unknown,
    TriState CheckpointRecorded = TriState.Unknown,
    TriState GuestIsDisposableVm = TriState.Unknown,
    TriState LabMutationGatesSatisfied = TriState.Unknown,
    TriState PlanFrozen = TriState.Unknown,
    TriState UpdateLowRisk = TriState.Unknown,
    TriState UpdateWindowsRecommended = TriState.Unknown,
    TriState SourceIsWindowsUpdate = TriState.Unknown,
    TriState AuthoritativeHardwareIdMatch = TriState.Unknown,
    TriState BrowseOnlyKnownAndFalse = TriState.Unknown,
    TriState PackageDownloadedAndVerified = TriState.Unknown,
    TriState PendingRebootNotPending = TriState.Unknown,
    TriState BrokerToctouChecksPassed = TriState.Unknown,
    TriState ServicingLockAcquired = TriState.Unknown,
    TriState NoRestrictedDeviceCategory = TriState.Unknown);

public static class VmHarnessDualVmProof
{
    public static VmHarnessDualVmProofResult Evaluate(
        VmHarnessHostVmProof? hostProof,
        VmHarnessGuestEnvironmentProof? guestProof)
    {
        if (hostProof is null)
        {
            return new VmHarnessDualVmProofResult(
                false,
                guestProof?.IsDisposableVm ?? false,
                false,
                null,
                guestProof,
                "Host Hyper-V proof is missing.");
        }

        if (guestProof is null)
        {
            return new VmHarnessDualVmProofResult(
                true,
                false,
                false,
                hostProof,
                null,
                "Guest environment proof is missing.");
        }

        var hostValid = !string.IsNullOrWhiteSpace(hostProof.HyperVVmId);
        var guestValid = guestProof.IsDisposableVm;
        var agree = hostValid && guestValid;

        return new VmHarnessDualVmProofResult(
            hostValid,
            guestValid,
            agree,
            hostProof,
            guestProof,
            agree ? null : "Host and guest VM proofs disagree; mutation blocked.");
    }
}

public static class VmHarnessVerdictClassifier
{
    public static VmHarnessVerdict ClassifyFinal(
        VmHarnessMutationOutcome mutationOutcome,
        VmHarnessRestoreOutcome restoreOutcome,
        VmHarnessScenarioResult? scenarioResult,
        bool mutationAttempted)
    {
        if (scenarioResult?.Verdict == VmHarnessVerdict.NoEligibleCandidate)
        {
            return VmHarnessVerdict.NoEligibleCandidate;
        }

        if (restoreOutcome == VmHarnessRestoreOutcome.Failed)
        {
            return VmHarnessVerdict.RestoreFailed;
        }

        if (scenarioResult?.Verdict == VmHarnessVerdict.RecoveryRequired
            || mutationOutcome == VmHarnessMutationOutcome.Inconclusive)
        {
            return VmHarnessVerdict.RecoveryRequired;
        }

        if (mutationOutcome == VmHarnessMutationOutcome.Blocked
            || mutationOutcome == VmHarnessMutationOutcome.Failed)
        {
            return VmHarnessVerdict.FailClosed;
        }

        if (!mutationAttempted && scenarioResult?.Verdict == VmHarnessVerdict.DryRunComplete)
        {
            return VmHarnessVerdict.DryRunComplete;
        }

        if (scenarioResult?.Verdict == VmHarnessVerdict.CheckpointRestoreProved)
        {
            return VmHarnessVerdict.CheckpointRestoreProved;
        }

        if (mutationOutcome == VmHarnessMutationOutcome.Succeeded
            && restoreOutcome == VmHarnessRestoreOutcome.Succeeded)
        {
            return VmHarnessVerdict.Pass;
        }

        if (mutationOutcome == VmHarnessMutationOutcome.NotAttempted
            && restoreOutcome == VmHarnessRestoreOutcome.Succeeded)
        {
            return VmHarnessVerdict.CheckpointRestoreProved;
        }

        return VmHarnessVerdict.HarnessError;
    }
}
