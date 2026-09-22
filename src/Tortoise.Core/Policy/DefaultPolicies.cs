using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Planning;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Policy;

public sealed class DefaultRiskPolicy : IRiskPolicy
{
    private static readonly HashSet<string> RestrictedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Firmware",
        "System",
        "SCSIAdapter",
        "HDC",
        "Volume",
        "Computer",
    };

    private static readonly HashSet<string> ManualReviewClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Display",
        "Net",
        "USB",
        "HIDClass",
        "System",
    };

    public DriverRiskLevel Classify(DriverCandidate candidate, DeviceContext context)
    {
        if (context.IsFirmwareRelated)
        {
            return DriverRiskLevel.Restricted;
        }

        if (RestrictedClasses.Contains(context.ClassName) || context.IsBootCritical)
        {
            return DriverRiskLevel.Restricted;
        }

        if (context.IsActiveNetworkAdapter || context.IsActiveDisplayAdapter ||
            ManualReviewClasses.Contains(context.ClassName))
        {
            return DriverRiskLevel.Moderate;
        }

        return DriverRiskLevel.Low;
    }
}

public sealed class DefaultUpdatePlanPolicy : IUpdatePlanPolicy
{
    public bool IsPlanStale(
        StoredUpdatePlan storedPlan,
        DeviceSnapshot currentDevice,
        InstalledDriver currentDriver) =>
        StalePlanDetector.IsStale(
            storedPlan.Plan,
            currentDevice,
            currentDriver,
            storedPlan.Classification,
            storedPlan.RiskLevel);
}
