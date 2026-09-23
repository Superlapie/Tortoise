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

public sealed record HarnessLabVmInstallJson(
    Guid PlanId,
    Guid? TransactionId,
    string? TransactionState,
    bool CompletedSuccessfully,
    string? VerificationResult,
    bool RebootRequired,
    bool AwaitingReboot,
    bool? BrokerSucceeded,
    int? BrokerResultCode,
    string? BrokerErrorCode,
    string Classification,
    string Summary);

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

    public static VmHarnessInnerLabGateEvidence BuildInnerGateEvidence(HarnessLabVmInstallJson result)
    {
        if (string.Equals(result.Classification, HarnessLabInstallClassification.PolicyBlocked.ToString(), StringComparison.Ordinal))
        {
            return new VmHarnessInnerLabGateEvidence(
                BrokerToctouChecksPassed: TriState.False,
                ServicingLockAcquired: TriState.False);
        }

        if (string.Equals(result.Classification, HarnessLabInstallClassification.Completed.ToString(), StringComparison.Ordinal)
            || string.Equals(result.Classification, HarnessLabInstallClassification.AwaitingReboot.ToString(), StringComparison.Ordinal))
        {
            return new VmHarnessInnerLabGateEvidence(
                PlanFrozen: TriState.True,
                UpdateLowRisk: TriState.True,
                UpdateWindowsRecommended: TriState.True,
                SourceIsWindowsUpdate: TriState.True,
                AuthoritativeHardwareIdMatch: TriState.True,
                BrowseOnlyKnownAndFalse: TriState.True,
                PackageDownloadedAndVerified: TriState.True,
                PendingRebootNotPending: result.AwaitingReboot ? TriState.False : TriState.True,
                BrokerToctouChecksPassed: TriState.True,
                ServicingLockAcquired: TriState.True,
                NoRestrictedDeviceCategory: TriState.True);
        }

        return new VmHarnessInnerLabGateEvidence();
    }
}
