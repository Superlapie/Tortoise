using Tortoise.Contracts.Mutation;
using Tortoise.Core.Planning;
using Tortoise.Core.Transactions;
using Tortoise.Core.Updates;

namespace Tortoise.Core.FaultInjection;

public enum FaultInjectionScenario
{
    ProcessCrash = 0,
    UnexpectedReboot = 1,
    NetworkFailure = 2,
    CandidateDisappeared = 3,
}

public enum FaultInjectionCheckpoint
{
    AfterPreflight = 0,
    DuringDownload = 1,
    DuringInstall = 2,
    AfterInstallBeforeVerification = 3,
}

public enum FaultReconciliationAction
{
    RescanAndRebuildPlan = 0,
    PrepareRecovery = 1,
    ResumeAfterReboot = 2,
    ManualReviewRequired = 3,
}

public sealed record FaultScenarioDefinition(
    FaultInjectionScenario Scenario,
    FaultInjectionCheckpoint Checkpoint,
    string Title,
    string Description);

public sealed record InjectedFaultResult(
    FaultInjectionScenario Scenario,
    FaultInjectionCheckpoint Checkpoint,
    UpdateTransactionState InterruptedState,
    bool RequiresRecovery,
    string Message,
    string RecoveryGuidance);

public sealed record FaultInjectionWorkflowResult(
    UpdateTransactionRecord Transaction,
    UpdatePreflight Preflight,
    InjectedFaultResult InjectedFault,
    string Summary);

public sealed record FaultReconciliationResult(
    Guid TransactionId,
    Guid PlanId,
    UpdateTransactionState LastState,
    FaultInjectionScenario? InjectedScenario,
    FaultReconciliationAction RecommendedAction,
    string Summary,
    string Guidance);

public interface IFaultInjectionWorkflowService
{
    Task<FaultInjectionWorkflowResult> RunScenarioAsync(
        Guid planId,
        FaultInjectionScenario scenario,
        CancellationToken cancellationToken = default);
}

public interface IFaultReconciliationService
{
    Task<FaultReconciliationResult> ReconcileAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);
}
