using Tortoise.Contracts.Elevation;
using Tortoise.Core.Planning;
using Tortoise.Core.Updates;

namespace Tortoise.Security.Broker;

public sealed class BrokerPlanAuthority : IBrokerPlanAuthority
{
    private readonly IUpdatePlanStore _planStore;

    public BrokerPlanAuthority(IUpdatePlanStore planStore)
    {
        _planStore = planStore;
    }

    public async Task<BrokerPlanAuthorityResult> ValidateAsync(
        Guid planId,
        string planHash,
        BrokerInstallPayload? installPayload,
        CancellationToken cancellationToken = default)
    {
        var storedPlan = await _planStore.GetPlanAsync(planId, cancellationToken);
        if (storedPlan is null)
        {
            return new BrokerPlanAuthorityResult(false, $"Plan '{planId}' was not found in persistent storage.");
        }

        var expectedHash = UpdatePlanFactory.ComputeHash(
            storedPlan.Plan.PlanId,
            storedPlan.Plan.DeviceSnapshot,
            storedPlan.Plan.CurrentDriver,
            storedPlan.Plan.ProposedUpdate,
            storedPlan.Plan.SafetyPolicyVersion,
            storedPlan.Classification,
            storedPlan.RiskLevel);

        if (!string.Equals(storedPlan.Plan.PlanHash, planHash, StringComparison.Ordinal))
        {
            return new BrokerPlanAuthorityResult(false, "Submitted plan hash does not match the persisted frozen plan.");
        }

        if (!string.Equals(expectedHash, planHash, StringComparison.Ordinal))
        {
            return new BrokerPlanAuthorityResult(false, "Persisted plan hash failed canonical recomputation.");
        }

        if (installPayload is not null)
        {
            var candidate = storedPlan.Plan.ProposedUpdate.Candidate;
            if (!string.Equals(candidate.UpdateIdentity, installPayload.UpdateId, StringComparison.OrdinalIgnoreCase)
                || candidate.UpdateRevision != installPayload.Revision)
            {
                return new BrokerPlanAuthorityResult(
                    false,
                    "Install payload update identity does not match the approved frozen plan candidate.");
            }

            if (storedPlan.Plan.ProposedUpdate.RequiresEula)
            {
                return new BrokerPlanAuthorityResult(
                    false,
                    "The approved plan requires EULA acceptance before installation.");
            }
        }

        return new BrokerPlanAuthorityResult(true, "Plan authorization succeeded.", storedPlan);
    }
}
