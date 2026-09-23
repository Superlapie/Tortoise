using Tortoise.VmHarness.Domain;

namespace Tortoise.VmHarness.Safety;

public static class VmHarnessHostPreGate
{
    public const int RequiredConditionCount = 5;

    private static readonly string[] ConditionNames =
    [
        "Target is a positively identified Hyper-V guest VM",
        "Host harness identifies the VM through the hypervisor with immutable VM ID",
        "A dedicated checkpoint exists with a recorded identifier for this run",
        "Guest disposable-VM detector reports IsDisposableVm=true",
        "Lab mutation capability is enabled in the guest",
    ];

    public static IReadOnlyList<string> ConditionDescriptions => ConditionNames;

    public static VmHarnessSafetyGateResult Evaluate(VmHarnessHostPreGateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var satisfied = new List<string>();
        var failed = new List<string>();
        var unknown = new List<string>();

        Evaluate(0, context.TargetIsHyperVGuest, satisfied, failed, unknown);
        Evaluate(1, context.HostHyperVProofValid, satisfied, failed, unknown);
        Evaluate(2, context.CheckpointRecorded, satisfied, failed, unknown);
        Evaluate(3, context.GuestIsDisposableVm, satisfied, failed, unknown);
        Evaluate(4, context.LabMutationCapabilityEnabled, satisfied, failed, unknown);

        return new VmHarnessSafetyGateResult(
            failed.Count == 0 && unknown.Count == 0,
            satisfied,
            failed,
            unknown);
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

public sealed record VmHarnessHostPreGateContext(
    TriState TargetIsHyperVGuest = TriState.Unknown,
    TriState HostHyperVProofValid = TriState.Unknown,
    TriState CheckpointRecorded = TriState.Unknown,
    TriState GuestIsDisposableVm = TriState.Unknown,
    TriState LabMutationCapabilityEnabled = TriState.Unknown);

public static class VmHarnessInnerLabGate
{
    public const int RequiredConditionCount = 11;

    private static readonly string[] ConditionNames =
    [
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

    public static VmHarnessSafetyGateResult EvaluateFromLabEvidence(VmHarnessInnerLabGateEvidence? evidence)
    {
        if (evidence is null)
        {
            return new VmHarnessSafetyGateResult(
                false,
                [],
                [],
                ConditionNames.ToList());
        }

        var satisfied = new List<string>();
        var failed = new List<string>();
        var unknown = new List<string>();

        Evaluate(0, evidence.PlanFrozen, satisfied, failed, unknown);
        Evaluate(1, evidence.UpdateLowRisk, satisfied, failed, unknown);
        Evaluate(2, evidence.UpdateWindowsRecommended, satisfied, failed, unknown);
        Evaluate(3, evidence.SourceIsWindowsUpdate, satisfied, failed, unknown);
        Evaluate(4, evidence.AuthoritativeHardwareIdMatch, satisfied, failed, unknown);
        Evaluate(5, evidence.BrowseOnlyKnownAndFalse, satisfied, failed, unknown);
        Evaluate(6, evidence.PackageDownloadedAndVerified, satisfied, failed, unknown);
        Evaluate(7, evidence.PendingRebootNotPending, satisfied, failed, unknown);
        Evaluate(8, evidence.BrokerToctouChecksPassed, satisfied, failed, unknown);
        Evaluate(9, evidence.ServicingLockAcquired, satisfied, failed, unknown);
        Evaluate(10, evidence.NoRestrictedDeviceCategory, satisfied, failed, unknown);

        return new VmHarnessSafetyGateResult(
            failed.Count == 0 && unknown.Count == 0,
            satisfied,
            failed,
            unknown);
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

public sealed record VmHarnessInnerLabGateEvidence(
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
