using Tortoise.VmHarness.Domain;
using Tortoise.VmHarness.Safety;

namespace Tortoise.VmHarness.Guest;

public enum HarnessLabInstallClassification
{
    Completed,
    AwaitingReboot,
    PolicyBlocked,
    RecoveryRequired,
    VerificationInconclusive,
    Failed,
}

public sealed record HarnessLabStatusJson(
    bool IsLabBuild,
    bool IsDisposableVm,
    bool MutationTestsEnabled,
    bool VmInstallExplicitlyAllowed,
    bool MutationCapabilityEnabled,
    string MutationEnvironment,
    string Reason);

public sealed record HarnessLabObservedInnerGateJson(
    string PlanFrozen = "Unknown",
    string UpdateLowRisk = "Unknown",
    string UpdateWindowsRecommended = "Unknown",
    string SourceIsWindowsUpdate = "Unknown",
    string AuthoritativeHardwareIdMatch = "Unknown",
    string BrowseOnlyKnownAndFalse = "Unknown",
    string PackageDownloadedAndVerified = "Unknown",
    string PreInstallPendingRebootNotPending = "Unknown",
    string BrokerToctouChecksPassed = "Unknown",
    string ServicingLockAcquired = "Unknown",
    string NoRestrictedDeviceCategory = "Unknown");

public sealed record HarnessLabVmPreflightJson(
    Guid PlanId,
    bool IsBlocked,
    HarnessLabObservedInnerGateJson InnerGateEvidence);

public sealed record HarnessLabVmInstallJson(
    Guid PlanId,
    Guid? TransactionId,
    string? TransactionState,
    bool CompletedSuccessfully,
    string? VerificationResult,
    bool PostInstallRebootRequired,
    bool AwaitingReboot,
    bool? BrokerSucceeded,
    int? BrokerResultCode,
    string? BrokerErrorCode,
    string Classification,
    string Summary,
    HarnessLabObservedInnerGateJson? InnerGateEvidence);

public static class HarnessLabJsonParser
{
    public static HarnessLabStatusJson ParseStatusJson(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<HarnessLabStatusJson>(
            json,
            JsonOptions)
        ?? throw new InvalidOperationException("Lab status JSON was empty or invalid.");

    public static HarnessLabVmInstallJson ParseInstallJson(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<HarnessLabVmInstallJson>(
            json,
            JsonOptions)
        ?? throw new InvalidOperationException("Lab install JSON was empty or invalid.");

    public static HarnessLabVmPreflightJson ParsePreflightJson(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<HarnessLabVmPreflightJson>(
            json,
            JsonOptions)
        ?? throw new InvalidOperationException("Lab preflight JSON was empty or invalid.");

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

public static class LabInstallResultClassifier
{
    public static (VmHarnessVerdict Verdict, VmHarnessMutationOutcome Outcome) Classify(HarnessLabVmInstallJson result)
    {
        if (!Enum.TryParse<HarnessLabInstallClassification>(result.Classification, out var classification))
        {
            classification = HarnessLabInstallClassification.Failed;
        }

        return classification switch
        {
            HarnessLabInstallClassification.Completed => (VmHarnessVerdict.Pass, VmHarnessMutationOutcome.Succeeded),
            HarnessLabInstallClassification.AwaitingReboot => (VmHarnessVerdict.RecoveryRequired, VmHarnessMutationOutcome.Inconclusive),
            HarnessLabInstallClassification.PolicyBlocked => (VmHarnessVerdict.FailClosed, VmHarnessMutationOutcome.Blocked),
            HarnessLabInstallClassification.RecoveryRequired => (VmHarnessVerdict.RecoveryRequired, VmHarnessMutationOutcome.Inconclusive),
            HarnessLabInstallClassification.VerificationInconclusive => (VmHarnessVerdict.RecoveryRequired, VmHarnessMutationOutcome.Inconclusive),
            _ => (VmHarnessVerdict.HarnessError, VmHarnessMutationOutcome.Failed),
        };
    }

    public static VmHarnessInnerLabGateEvidence MapObservedInnerGate(HarnessLabObservedInnerGateJson? evidence)
    {
        if (evidence is null)
        {
            return new VmHarnessInnerLabGateEvidence();
        }

        return new VmHarnessInnerLabGateEvidence(
            ParseTriState(evidence.PlanFrozen),
            ParseTriState(evidence.UpdateLowRisk),
            ParseTriState(evidence.UpdateWindowsRecommended),
            ParseTriState(evidence.SourceIsWindowsUpdate),
            ParseTriState(evidence.AuthoritativeHardwareIdMatch),
            ParseTriState(evidence.BrowseOnlyKnownAndFalse),
            ParseTriState(evidence.PackageDownloadedAndVerified),
            ParseTriState(evidence.PreInstallPendingRebootNotPending),
            ParseTriState(evidence.BrokerToctouChecksPassed),
            ParseTriState(evidence.ServicingLockAcquired),
            ParseTriState(evidence.NoRestrictedDeviceCategory));
    }

    private static TriState ParseTriState(string? value) =>
        value?.Trim() switch
        {
            "True" => TriState.True,
            "False" => TriState.False,
            _ => TriState.Unknown,
        };
}
