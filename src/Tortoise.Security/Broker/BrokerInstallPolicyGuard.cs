using Tortoise.Core.Planning;
using Tortoise.Core.Updates;

namespace Tortoise.Security.Broker;

public static class BrokerInstallPolicyGuard
{
    public static BrokerPlanAuthorityResult ValidateStoredPlanPolicy(StoredUpdatePlan storedPlan)
    {
        if (storedPlan.RiskLevel != DriverRiskLevel.Low)
        {
            return Deny(
                "Broker rejected the plan because only low-risk updates may be installed through the elevated boundary.");
        }

        if (storedPlan.Classification != UpdateClassification.WindowsRecommended)
        {
            return Deny(
                "Broker rejected the plan because only Windows-recommended updates may be installed through the elevated boundary.");
        }

        if (storedPlan.Plan.ProposedUpdate.RequiresEula)
        {
            return Deny("Broker rejected the plan because EULA acceptance is required before installation.");
        }

        return new BrokerPlanAuthorityResult(true, "Broker install policy checks passed.", storedPlan);
    }

    private static BrokerPlanAuthorityResult Deny(string message) =>
        new(false, message);
}
