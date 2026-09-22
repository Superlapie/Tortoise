using Tortoise.Contracts.Elevation;
using Tortoise.Core.Devices;
using Tortoise.Core.Planning;
using Tortoise.Core.Updates;
using Tortoise.Security.Broker;

namespace Tortoise.Broker.Validation;

public interface IBrokerLiveInstallVerifier
{
    Task<BrokerPlanAuthorityResult> VerifyInstallBoundaryAsync(
        StoredUpdatePlan storedPlan,
        BrokerInstallPayload payload,
        CancellationToken cancellationToken = default);
}

public sealed class BrokerLiveInstallVerifier : IBrokerLiveInstallVerifier
{
    private readonly IDeviceInventoryProvider? _deviceInventoryProvider;
    private readonly ILiveWindowsUpdateCandidateProvider _candidateProvider;

    public BrokerLiveInstallVerifier(
        IDeviceInventoryProvider? deviceInventoryProvider,
        ILiveWindowsUpdateCandidateProvider candidateProvider)
    {
        _deviceInventoryProvider = deviceInventoryProvider;
        _candidateProvider = candidateProvider;
    }

    public async Task<BrokerPlanAuthorityResult> VerifyInstallBoundaryAsync(
        StoredUpdatePlan storedPlan,
        BrokerInstallPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (_deviceInventoryProvider is null)
        {
            return new BrokerPlanAuthorityResult(
                false,
                "Live device inventory is unavailable for broker TOCTOU verification.");
        }

        var inventory = await _deviceInventoryProvider.ScanAsync(cancellationToken);
        var liveDevice = inventory.Devices.SingleOrDefault(device =>
            string.Equals(
                device.Snapshot.Identity.DeviceInstanceId,
                storedPlan.Plan.DeviceSnapshot.Identity.DeviceInstanceId,
                StringComparison.OrdinalIgnoreCase));

        var liveCandidate = await _candidateProvider.TryGetCandidateAsync(
            payload.UpdateId,
            payload.Revision,
            cancellationToken);

        return BrokerInstallToctouGuard.VerifyInstallBoundary(
            storedPlan,
            payload,
            liveDevice,
            liveCandidate);
    }
}

public interface ILiveWindowsUpdateCandidateProvider
{
    Task<WindowsUpdateCandidate?> TryGetCandidateAsync(
        string updateId,
        int revision,
        CancellationToken cancellationToken = default);
}
