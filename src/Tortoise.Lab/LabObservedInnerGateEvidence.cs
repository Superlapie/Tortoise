using System.Text.Json.Serialization;
using Tortoise.Core.Devices;
using Tortoise.Core.Installation;
using Tortoise.Core.Planning;
using Tortoise.Core.Transactions;
using Tortoise.Core.Updates;

namespace Tortoise.Lab;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LabTriState
{
    Unknown = 0,
    False = 1,
    True = 2,
}

public sealed record LabObservedInnerGateEvidence(
    LabTriState PlanFrozen = LabTriState.Unknown,
    LabTriState UpdateLowRisk = LabTriState.Unknown,
    LabTriState UpdateWindowsRecommended = LabTriState.Unknown,
    LabTriState SourceIsWindowsUpdate = LabTriState.Unknown,
    LabTriState AuthoritativeHardwareIdMatch = LabTriState.Unknown,
    LabTriState BrowseOnlyKnownAndFalse = LabTriState.Unknown,
    LabTriState PackageDownloadedAndVerified = LabTriState.Unknown,
    LabTriState PreInstallPendingRebootNotPending = LabTriState.Unknown,
    LabTriState BrokerToctouChecksPassed = LabTriState.Unknown,
    LabTriState ServicingLockAcquired = LabTriState.Unknown,
    LabTriState NoRestrictedDeviceCategory = LabTriState.Unknown);

public static class LabInnerGateEvidenceAssembler
{
    public static LabObservedInnerGateEvidence FromPreflight(
        StoredUpdatePlan plan,
        UpdatePreflight preflight)
    {
        return new LabObservedInnerGateEvidence(
            PlanFrozen: plan.IsFrozen ? LabTriState.True : LabTriState.False,
            UpdateLowRisk: plan.RiskLevel == DriverRiskLevel.Low ? LabTriState.True : LabTriState.False,
            UpdateWindowsRecommended: plan.Classification == UpdateClassification.WindowsRecommended
                ? LabTriState.True
                : LabTriState.False,
            SourceIsWindowsUpdate: FromPreflightCheck(preflight, "update-source"),
            NoRestrictedDeviceCategory: FromClassificationCheck(preflight, plan));
    }

    public static LabObservedInnerGateEvidence FromInstallAttempt(
        StoredUpdatePlan plan,
        UpdatePreflight? preflight,
        UpdateTransactionRecord? transactionRecord,
        BrokerDriverInstallResult? brokerInstall,
        string? failureSummary)
    {
        var baseEvidence = preflight is not null
            ? FromPreflight(plan, preflight)
            : new LabObservedInnerGateEvidence(
                PlanFrozen: plan.IsFrozen ? LabTriState.True : LabTriState.False,
                UpdateLowRisk: plan.RiskLevel == DriverRiskLevel.Low ? LabTriState.True : LabTriState.False,
                UpdateWindowsRecommended: plan.Classification == UpdateClassification.WindowsRecommended
                    ? LabTriState.True
                    : LabTriState.False);

        var journalStates = (transactionRecord?.Journal ?? [])
            .Select(entry => entry.ToState)
            .ToHashSet();

        if (journalStates.Contains(UpdateTransactionState.PreflightFailed)
            && ContainsMessage(transactionRecord, "not eligible for disposable VM install"))
        {
            return baseEvidence with
            {
                UpdateLowRisk = LabTriState.False,
                UpdateWindowsRecommended = LabTriState.False,
            };
        }

        var packageEvidence = journalStates.Contains(UpdateTransactionState.Verified)
            ? LabTriState.True
            : journalStates.Contains(UpdateTransactionState.Failed)
                && (journalStates.Contains(UpdateTransactionState.Downloading)
                    || journalStates.Contains(UpdateTransactionState.VerificationRunning))
                ? LabTriState.False
                : LabTriState.Unknown;

        var brokerMessage = brokerInstall?.Message ?? failureSummary ?? string.Empty;
        var brokerFailed = brokerInstall?.Succeeded == false;
        var brokerSucceeded = brokerInstall?.Succeeded == true;
        var brokerReached = journalStates.Contains(UpdateTransactionState.Installing)
            || journalStates.Contains(UpdateTransactionState.InstallReturned)
            || brokerInstall is not null;

        return baseEvidence with
        {
            PackageDownloadedAndVerified = packageEvidence,
            PreInstallPendingRebootNotPending = EvaluatePreInstallPendingReboot(
                brokerSucceeded,
                brokerFailed,
                brokerMessage,
                brokerReached),
            BrokerToctouChecksPassed = EvaluateBrokerGate(
                brokerSucceeded,
                brokerFailed,
                brokerMessage,
                brokerReached,
                IsToctouFailure),
            ServicingLockAcquired = EvaluateBrokerGate(
                brokerSucceeded,
                brokerFailed,
                brokerMessage,
                brokerReached,
                IsServicingLockFailure),
            AuthoritativeHardwareIdMatch = EvaluateBrokerGate(
                brokerSucceeded,
                brokerFailed,
                brokerMessage,
                brokerReached,
                IsHardwareIdFailure),
            BrowseOnlyKnownAndFalse = EvaluateBrokerGate(
                brokerSucceeded,
                brokerFailed,
                brokerMessage,
                brokerReached,
                IsBrowseOnlyFailure),
        };
    }

    private static LabTriState EvaluatePreInstallPendingReboot(
        bool brokerSucceeded,
        bool brokerFailed,
        string brokerMessage,
        bool brokerReached)
    {
        if (brokerSucceeded)
        {
            return LabTriState.True;
        }

        if (brokerFailed && IsPendingRebootFailure(brokerMessage))
        {
            return LabTriState.False;
        }

        return brokerReached ? LabTriState.Unknown : LabTriState.Unknown;
    }

    private static LabTriState EvaluateBrokerGate(
        bool brokerSucceeded,
        bool brokerFailed,
        string brokerMessage,
        bool brokerReached,
        Func<string, bool> failureMatcher)
    {
        if (brokerSucceeded)
        {
            return LabTriState.True;
        }

        if (brokerFailed && failureMatcher(brokerMessage))
        {
            return LabTriState.False;
        }

        return brokerReached ? LabTriState.Unknown : LabTriState.Unknown;
    }

    private static LabTriState FromPreflightCheck(UpdatePreflight preflight, string checkName)
    {
        var check = preflight.Checks.FirstOrDefault(item =>
            string.Equals(item.Name, checkName, StringComparison.OrdinalIgnoreCase));
        if (check is null)
        {
            return LabTriState.Unknown;
        }

        return check.Result switch
        {
            PreflightResult.Passed => LabTriState.True,
            PreflightResult.Blocked => LabTriState.False,
            _ => LabTriState.Unknown,
        };
    }

    private static LabTriState FromClassificationCheck(UpdatePreflight preflight, StoredUpdatePlan plan)
    {
        if (plan.Classification == UpdateClassification.Restricted)
        {
            return LabTriState.False;
        }

        return FromPreflightCheck(preflight, "update-classification") switch
        {
            LabTriState.False => LabTriState.False,
            LabTriState.True => LabTriState.True,
            _ => plan.Classification == UpdateClassification.Restricted
                ? LabTriState.False
                : LabTriState.Unknown,
        };
    }

    private static bool ContainsMessage(UpdateTransactionRecord? transactionRecord, string fragment) =>
        transactionRecord?.Journal.Any(entry =>
            entry.Message?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true) == true;

    private static bool IsPendingRebootFailure(string message) =>
        message.Contains("pending reboot", StringComparison.OrdinalIgnoreCase)
        || message.Contains("pending-reboot", StringComparison.OrdinalIgnoreCase);

    private static bool IsServicingLockFailure(string message) =>
        message.Contains("servicing operation", StringComparison.OrdinalIgnoreCase)
        || message.Contains("servicing lock", StringComparison.OrdinalIgnoreCase);

    private static bool IsToctouFailure(string message) =>
        message.Contains("TOCTOU", StringComparison.OrdinalIgnoreCase)
        || message.Contains("hardware ID", StringComparison.OrdinalIgnoreCase)
        || message.Contains("no longer present", StringComparison.OrdinalIgnoreCase)
        || message.Contains("changed since the plan", StringComparison.OrdinalIgnoreCase)
        || message.Contains("no longer returns", StringComparison.OrdinalIgnoreCase)
        || message.Contains("does not match", StringComparison.OrdinalIgnoreCase);

    private static bool IsHardwareIdFailure(string message) =>
        message.Contains("hardware ID", StringComparison.OrdinalIgnoreCase);

    private static bool IsBrowseOnlyFailure(string message) =>
        message.Contains("BrowseOnly", StringComparison.OrdinalIgnoreCase)
        || message.Contains("browse only", StringComparison.OrdinalIgnoreCase);
}
