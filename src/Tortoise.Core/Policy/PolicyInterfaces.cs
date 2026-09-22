using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Planning;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Policy;

public interface IRiskPolicy
{
    DriverRiskLevel Classify(DriverCandidate candidate, DeviceContext context);
}

public interface IUpdateSourcePolicy
{
    bool IsSourceAllowed(DriverSource source);

    UpdateClassification ClassifyUpdate(DriverUpdate update, DeviceContext context);
}

public interface IUpdatePlanPolicy
{
    bool IsPlanStale(
        StoredUpdatePlan storedPlan,
        DeviceSnapshot currentDevice,
        InstalledDriver currentDriver);
}

public sealed record DeviceContext(
    string DeviceInstanceId,
    string ClassName,
    bool IsBootCritical,
    bool IsActiveNetworkAdapter,
    bool IsActiveDisplayAdapter,
    bool IsFirmwareRelated);

public sealed class DefaultUpdateSourcePolicy : IUpdateSourcePolicy
{
    public bool IsSourceAllowed(DriverSource source) =>
        source.IsTrusted && source.Kind is DriverSourceKind.WindowsUpdate or DriverSourceKind.Inbox;

    public UpdateClassification ClassifyUpdate(DriverUpdate update, DeviceContext context)
    {
        if (context.IsFirmwareRelated)
        {
            return UpdateClassification.Restricted;
        }

        return update.Classification;
    }
}
