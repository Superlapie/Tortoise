using System.Text.Json;
using System.Text.Json.Serialization;
using Tortoise.Contracts.Mutation;
using Tortoise.Core.Installation;
using Tortoise.Core.Mutation;
using Tortoise.Core.Planning;
using Tortoise.Core.Transactions;
using Tortoise.Core.Updates;
using Tortoise.WindowsUpdate.Environment;

namespace Tortoise.Lab;

public static class LabMachineReadableOutput
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void WriteStatusJson(IExecutionEnvironmentDetector detector)
    {
        var environment = detector.Detect();
        var capability = MutationCapabilityResolver.Resolve(environment);
        var payload = new LabStatusJson(
            IsLabBuild: MutationBuildPolicy.IsLabBuild,
            IsDisposableVm: environment.IsDisposableVm,
            MutationTestsEnabled: environment.MutationTestsEnabled,
            VmInstallExplicitlyAllowed: environment.VmInstallExplicitlyAllowed,
            MutationCapabilityEnabled: capability.IsEnabled,
            MutationEnvironment: capability.Environment.ToString(),
            Reason: capability.Reason);
        Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    public static void WritePreflightJson(
        StoredUpdatePlan plan,
        UpdatePreflight preflight,
        LabObservedInnerGateEvidence innerGateEvidence)
    {
        var payload = new LabVmPreflightJson(
            PlanId: plan.PlanId,
            IsBlocked: preflight.IsBlocked,
            Checks: preflight.Checks
                .Select(check => new LabPreflightCheckJson(
                    check.Name,
                    check.Result.ToString(),
                    check.Message))
                .ToList(),
            InnerGateEvidence: innerGateEvidence);
        Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    public static void WriteEvidenceSnapshotJson(LabEvidenceSnapshotJson snapshot) =>
        Console.WriteLine(JsonSerializer.Serialize(snapshot, JsonOptions));

    public static void WriteInstallJson(
        VmDriverInstallResult result,
        UpdateTransactionRecord? transactionRecord,
        LabInstallClassification classification,
        LabObservedInnerGateEvidence innerGateEvidence,
        StoredUpdatePlan? storedPlan = null)
    {
        var transaction = transactionRecord?.Transaction;
        var postInstallRebootRequired = transaction?.State == UpdateTransactionState.AwaitingReboot
            || result.PostInstallVerification.Result == UpdateVerificationResult.InstalledRestartRequired
            || result.BrokerInstall?.RebootRequired == true;

        var payload = new LabVmInstallJson(
            PlanId: result.PlanId,
            TransactionId: transaction?.TransactionId,
            TransactionState: transaction?.State.ToString(),
            CompletedSuccessfully: result.CompletedSuccessfully,
            VerificationResult: result.PostInstallVerification.Result.ToString(),
            PostInstallRebootRequired: postInstallRebootRequired,
            AwaitingReboot: transaction?.State == UpdateTransactionState.AwaitingReboot,
            BrokerSucceeded: result.BrokerInstall?.Succeeded,
            BrokerResultCode: result.BrokerInstall?.ResultCode,
            BrokerErrorCode: result.BrokerInstall?.ErrorCode,
            Classification: classification.ToString(),
            Summary: result.Summary,
            InnerGateEvidence: innerGateEvidence,
            SelectedPlan: storedPlan is null
                ? null
                : new LabSelectedPlanJson(
                    storedPlan.PlanId,
                    storedPlan.RiskLevel.ToString(),
                    storedPlan.Classification.ToString(),
                    storedPlan.IsFrozen));
        Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    public static void WriteInstallDeniedJson(string reason, Guid? planId = null)
    {
        var payload = new LabVmInstallJson(
            PlanId: planId ?? Guid.Empty,
            TransactionId: null,
            TransactionState: null,
            CompletedSuccessfully: false,
            VerificationResult: null,
            PostInstallRebootRequired: false,
            AwaitingReboot: false,
            BrokerSucceeded: false,
            BrokerResultCode: null,
            BrokerErrorCode: null,
            Classification: LabInstallClassification.PolicyBlocked.ToString(),
            Summary: reason,
            InnerGateEvidence: new LabObservedInnerGateEvidence());
        Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    public static LabInstallClassification Classify(
        VmDriverInstallResult result,
        UpdateTransactionRecord? transactionRecord)
    {
        var transactionState = transactionRecord?.Transaction.State;
        if (transactionState == UpdateTransactionState.AwaitingReboot
            || result.PostInstallVerification.Result == UpdateVerificationResult.InstalledRestartRequired
            || result.BrokerInstall?.RebootRequired == true)
        {
            return LabInstallClassification.AwaitingReboot;
        }

        if (result.CompletedSuccessfully)
        {
            return LabInstallClassification.Completed;
        }

        if (result.PostInstallVerification.Result == UpdateVerificationResult.InstalledInconclusive)
        {
            return LabInstallClassification.VerificationInconclusive;
        }

        if (transactionState == UpdateTransactionState.RecoveryRequired)
        {
            return LabInstallClassification.RecoveryRequired;
        }

        if (transactionState is UpdateTransactionState.PreflightFailed
            or UpdateTransactionState.Failed
            or UpdateTransactionState.Cancelled)
        {
            return LabInstallClassification.PolicyBlocked;
        }

        return LabInstallClassification.Failed;
    }
}

public enum LabInstallClassification
{
    Completed = 0,
    AwaitingReboot = 1,
    PolicyBlocked = 2,
    RecoveryRequired = 3,
    VerificationInconclusive = 4,
    Failed = 5,
}

public sealed record LabStatusJson(
    bool IsLabBuild,
    bool IsDisposableVm,
    bool MutationTestsEnabled,
    bool VmInstallExplicitlyAllowed,
    bool MutationCapabilityEnabled,
    string MutationEnvironment,
    string Reason);

public sealed record LabPreflightCheckJson(
    string Name,
    string Result,
    string Message);

public sealed record LabVmPreflightJson(
    Guid PlanId,
    bool IsBlocked,
    IReadOnlyList<LabPreflightCheckJson> Checks,
    LabObservedInnerGateEvidence InnerGateEvidence);

public sealed record LabSelectedPlanJson(
    Guid PlanId,
    string RiskLevel,
    string Classification,
    bool IsFrozen);

public sealed record LabVmInstallJson(
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
    LabObservedInnerGateEvidence InnerGateEvidence,
    LabSelectedPlanJson? SelectedPlan = null);
