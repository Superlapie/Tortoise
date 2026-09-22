using Tortoise.Core.Planning;
using Tortoise.Core.Transactions;

namespace Tortoise.Core.FaultInjection;

public sealed class FaultReconciliationService : IFaultReconciliationService
{
    private readonly IUpdateTransactionStore _transactionStore;
    private readonly IUpdatePlanService _planService;

    public FaultReconciliationService(
        IUpdateTransactionStore transactionStore,
        IUpdatePlanService planService)
    {
        _transactionStore = transactionStore;
        _planService = planService;
    }

    public async Task<FaultReconciliationResult> ReconcileAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        var record = await _transactionStore.GetAsync(transactionId, cancellationToken)
            ?? throw new InvalidOperationException($"Transaction '{transactionId}' was not found.");

        var plan = await _planService.GetPlanAsync(record.Transaction.PlanId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Plan '{record.Transaction.PlanId}' for transaction '{transactionId}' was not found.");

        var scenario = FaultInjectionJournal.TryParseScenario(record.Journal);
        var lastState = record.Transaction.State;

        if (scenario is null)
        {
            return new FaultReconciliationResult(
                transactionId,
                plan.PlanId,
                lastState,
                InjectedScenario: null,
                FaultReconciliationAction.ManualReviewRequired,
                Summary: "No fault injection marker was found in the transaction journal.",
                Guidance: "Inspect the journal manually or rerun a fault scenario with TORTOISE_FAULT_INJECTION=1.");
        }

        var (action, summary, guidance) = scenario.Value switch
        {
            FaultInjectionScenario.ProcessCrash => (
                FaultReconciliationAction.PrepareRecovery,
                "Process crash reconciliation recommends recovery preparation before any retry.",
                "Run recovery preparation for the plan, inspect the device state, and only then retry in a disposable VM."),
            FaultInjectionScenario.UnexpectedReboot => (
                FaultReconciliationAction.ResumeAfterReboot,
                "Unexpected reboot reconciliation recommends resuming post-install verification.",
                "Confirm the reboot completed, rescan the device inventory, and rerun verification for the plan."),
            FaultInjectionScenario.NetworkFailure => (
                FaultReconciliationAction.RescanAndRebuildPlan,
                "Network failure reconciliation recommends a fresh Windows Update scan.",
                "Restore network connectivity, run a new scan, and rebuild the frozen plan."),
            FaultInjectionScenario.CandidateDisappeared => (
                FaultReconciliationAction.RescanAndRebuildPlan,
                "Candidate disappearance reconciliation requires a fresh plan.",
                "Rescan Windows Update. If the driver is still applicable, create a new plan before retrying."),
            _ => (
                FaultReconciliationAction.ManualReviewRequired,
                "Unknown fault scenario marker detected.",
                "Review the transaction journal and plan manually."),
        };

        return new FaultReconciliationResult(
            transactionId,
            plan.PlanId,
            lastState,
            scenario,
            action,
            summary,
            guidance);
    }
}
