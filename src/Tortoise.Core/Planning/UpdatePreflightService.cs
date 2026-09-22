using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Policy;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Planning;

public sealed record PreflightContext(bool ForSimulation = false);

public interface IUpdatePreflightService
{
    UpdatePreflight RunPreflight(
        StoredUpdatePlan storedPlan,
        DeviceInventoryEntry currentDevice,
        IMutationCapability mutationCapability,
        PreflightContext? context = null);
}

public sealed class UpdatePreflightService : IUpdatePreflightService
{
    private readonly IUpdatePlanPolicy _planPolicy;
    private readonly IUpdateSourcePolicy _sourcePolicy;

    public UpdatePreflightService()
        : this(new DefaultUpdatePlanPolicy(), new DefaultUpdateSourcePolicy())
    {
    }

    public UpdatePreflightService(IUpdatePlanPolicy planPolicy, IUpdateSourcePolicy sourcePolicy)
    {
        _planPolicy = planPolicy;
        _sourcePolicy = sourcePolicy;
    }

    public UpdatePreflight RunPreflight(
        StoredUpdatePlan storedPlan,
        DeviceInventoryEntry currentDevice,
        IMutationCapability mutationCapability,
        PreflightContext? context = null)
    {
        context ??= new PreflightContext();
        var checks = new List<UpdatePreflightCheck>
        {
            EvaluatePlanIntegrity(storedPlan),
            EvaluateMutationCapability(mutationCapability, context),
            EvaluatePlanStaleness(storedPlan, currentDevice),
            EvaluateClassification(storedPlan),
            EvaluateSourcePolicy(storedPlan),
            EvaluateDevicePresence(currentDevice),
        };

        var isBlocked = checks.Any(check => check.Result == PreflightResult.Blocked);

        return new UpdatePreflight(storedPlan.PlanId, checks, isBlocked);
    }

    private static UpdatePreflightCheck EvaluatePlanIntegrity(StoredUpdatePlan storedPlan)
    {
        var expectedHash = UpdatePlanFactory.ComputeHash(
            storedPlan.Plan.PlanId,
            storedPlan.Plan.DeviceSnapshot,
            storedPlan.Plan.CurrentDriver,
            storedPlan.Plan.ProposedUpdate,
            storedPlan.Plan.SafetyPolicyVersion,
            storedPlan.Classification,
            storedPlan.RiskLevel);

        if (string.Equals(storedPlan.Plan.PlanHash, expectedHash, StringComparison.Ordinal))
        {
            return Passed("plan-integrity", "Plan hash matches the frozen plan contents.");
        }

        return Blocked("plan-integrity", "Plan hash does not match the frozen plan contents.");
    }

    private UpdatePreflightCheck EvaluateMutationCapability(
        IMutationCapability mutationCapability,
        PreflightContext context)
    {
        if (mutationCapability.IsEnabled)
        {
            return Passed("mutation-capability", "Mutation capability is enabled for this environment.");
        }

        if (context.ForSimulation)
        {
            return Warning(
                "mutation-capability",
                $"Simulation only: {mutationCapability.Reason}");
        }

        return Blocked("mutation-capability", mutationCapability.Reason);
    }

    private UpdatePreflightCheck EvaluatePlanStaleness(
        StoredUpdatePlan storedPlan,
        DeviceInventoryEntry currentDevice)
    {
        var currentDriver = RecommendationPlanMapper.ToInstalledDriver(currentDevice);
        if (_planPolicy.IsPlanStale(storedPlan, currentDevice.Snapshot, currentDriver))
        {
            return Blocked(
                "plan-staleness",
                "The frozen plan no longer matches the installed driver or device state.");
        }

        return Passed("plan-staleness", "Plan still matches the current device and driver state.");
    }

    private static UpdatePreflightCheck EvaluateClassification(StoredUpdatePlan storedPlan)
    {
        return storedPlan.Classification switch
        {
            UpdateClassification.Restricted => Blocked(
                "update-classification",
                "This update classification is restricted by Tortoise policy."),
            UpdateClassification.ReviewRequired => Warning(
                "update-classification",
                "Manual review is required before this update could be approved."),
            _ => Passed(
                "update-classification",
                $"Classification '{storedPlan.Classification}' is eligible for planning."),
        };
    }

    private UpdatePreflightCheck EvaluateSourcePolicy(StoredUpdatePlan storedPlan)
    {
        var source = storedPlan.Plan.ProposedUpdate.Candidate.Package.Source;
        if (_sourcePolicy.IsSourceAllowed(source))
        {
            return Passed("update-source", $"Source '{source.DisplayName}' is allowed.");
        }

        return Blocked("update-source", $"Source '{source.DisplayName}' is not allowed.");
    }

    private static UpdatePreflightCheck EvaluateDevicePresence(DeviceInventoryEntry currentDevice)
    {
        if (!currentDevice.Snapshot.Identity.IsPresent)
        {
            return Blocked("device-presence", "The target device is not present.");
        }

        if (currentDevice.Snapshot.Health.State == DeviceHealthState.Disabled)
        {
            return Warning("device-presence", "The target device is currently disabled.");
        }

        return Passed("device-presence", "The target device is present.");
    }

    private static UpdatePreflightCheck Passed(string name, string message) =>
        new(name, PreflightResult.Passed, message);

    private static UpdatePreflightCheck Warning(string name, string message) =>
        new(name, PreflightResult.Warning, message);

    private static UpdatePreflightCheck Blocked(string name, string message) =>
        new(name, PreflightResult.Blocked, message);
}
