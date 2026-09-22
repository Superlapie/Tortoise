using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Policy;
using Tortoise.Core.Recommendations;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Planning;

public sealed class UpdatePlanBuilder : IUpdatePlanBuilder
{
    public IReadOnlyList<StoredUpdatePlan> BuildPlans(
        RecommendationScanResult scanResult,
        UpdatePlanningOptions options)
    {
        var plans = new List<StoredUpdatePlan>();

        foreach (var recommendation in scanResult.Recommendations)
        {
            if (options.DeviceInstanceId is not null
                && !string.Equals(
                    recommendation.Device.Snapshot.Identity.DeviceInstanceId,
                    options.DeviceInstanceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (recommendation.ApplicableUpdate is null
                || recommendation.Classification is UpdateClassification.Current
                || recommendation.Classification is UpdateClassification.Unknown
                || recommendation.Classification is UpdateClassification.Restricted)
            {
                continue;
            }

            if (!options.IncludeOptionalUpdates
                && recommendation.Classification is UpdateClassification.WindowsOptional)
            {
                continue;
            }

            var plan = CreateStoredPlan(recommendation, options.ScanSessionId);
            plans.Add(plan);
        }

        return plans;
    }

    internal static StoredUpdatePlan CreateStoredPlan(
        DeviceUpdateRecommendation recommendation,
        long? scanSessionId)
    {
        var device = recommendation.Device;
        var proposedUpdate = RecommendationPlanMapper.ToDriverUpdate(recommendation);
        var currentDriver = RecommendationPlanMapper.ToInstalledDriver(device);
        var restartExpected = recommendation.ApplicableUpdate?.RestartRequired ?? false;

        var plan = UpdatePlanFactory.Create(
            device.Snapshot,
            currentDriver,
            proposedUpdate,
            TortoisePolicyVersions.SafetyPolicyVersion,
            TortoisePolicyVersions.ApplicationVersion,
            restartExpected,
            recommendation.Classification,
            recommendation.RiskLevel);

        return new StoredUpdatePlan(
            plan.PlanId,
            scanSessionId,
            plan.CreatedAtUtc,
            recommendation.Classification,
            recommendation.RiskLevel,
            IsFrozen: true,
            plan);
    }
}

internal static class RecommendationPlanMapper
{
    public static InstalledDriver ToInstalledDriver(DeviceInventoryEntry device)
    {
        var binding = device.InstalledDriver
            ?? throw new InvalidOperationException(
                $"Device '{device.Snapshot.Identity.DeviceInstanceId}' has no installed driver metadata for planning.");

        var identity = device.Snapshot.Identity;
        var package = new DriverPackage(
            new DriverIdentity(
                binding.InfName ?? "unknown.inf",
                binding.InfName ?? "unknown.inf",
                binding.ProviderName,
                identity.ClassName,
                identity.ClassGuid,
                binding.DriverVersion ?? new Version(0, 0),
                binding.DriverDate,
                binding.ProviderName,
                null,
                null,
                identity.DeviceInstanceId),
            new DriverSignature(false, false, binding.ProviderName, null),
            new DriverSource(
                DriverSourceKind.Unknown,
                "installed",
                "Installed driver (provenance unverified)",
                false));

        return new InstalledDriver(package, identity.DeviceInstanceId, null);
    }

    public static DriverUpdate ToDriverUpdate(DeviceUpdateRecommendation recommendation)
    {
        var update = recommendation.ApplicableUpdate
            ?? throw new InvalidOperationException("Cannot plan an update without an applicable package.");

        var device = recommendation.Device;
        var providerName = WindowsUpdateCandidateIdentity.ResolveProviderName(update);
        var candidate = new DriverCandidate(
            new DriverPackage(
                new DriverIdentity(
                    update.Title,
                    update.Title,
                    providerName,
                    update.DriverClass ?? device.Snapshot.Identity.ClassName,
                    device.Snapshot.Identity.ClassGuid,
                    update.DriverVersion ?? new Version(0, 0),
                    update.DriverDate,
                    update.DriverManufacturer,
                    null,
                    update.UpdateId,
                    update.DriverModel),
                new DriverSignature(false, false, update.DriverManufacturer, null),
                new DriverSource(
                    DriverSourceKind.WindowsUpdate,
                    update.UpdateId,
                    "Windows Update (signature not yet verified)",
                    false)),
            device.Snapshot.Identity.DeviceInstanceId,
            update.UpdateId,
            update.Revision);

        return new DriverUpdate(
            candidate,
            recommendation.Classification,
            recommendation.RiskLevel,
            update.RestartRequired,
            update.RequiresEula,
            recommendation.Explanation);
    }
}
