using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Policy;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Recommendations;

public sealed class RecommendationEngine : IRecommendationEngine
{
    private readonly IRiskPolicy _riskPolicy;
    private readonly IUpdateSourcePolicy _sourcePolicy;

    public RecommendationEngine()
        : this(new DefaultRiskPolicy(), new DefaultUpdateSourcePolicy())
    {
    }

    public RecommendationEngine(IRiskPolicy riskPolicy, IUpdateSourcePolicy sourcePolicy)
    {
        _riskPolicy = riskPolicy;
        _sourcePolicy = sourcePolicy;
    }

    public RecommendationScanResult Evaluate(
        IReadOnlyList<DeviceInventoryEntry> devices,
        DriverUpdateScanResult updateScan,
        RecommendationOptions? options = null)
    {
        options ??= new RecommendationOptions();
        var warnings = new List<string>(updateScan.Warnings);
        var candidates = updateScan.Candidates.AsEnumerable();

        if (!options.IncludeOptionalUpdates)
        {
            candidates = candidates.Where(candidate =>
                candidate.Classification != UpdateClassification.WindowsOptional);
        }

        var candidateList = candidates.ToList();
        var assignments = UpdateAssignmentPlanner.AssignBestMatches(devices, candidateList);
        var recommendations = new List<DeviceUpdateRecommendation>();
        var matchedUpdateIds = assignments.Values
            .Select(match => match.Update.UpdateId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var device in devices)
        {
            var instanceId = device.Snapshot.Identity.DeviceInstanceId;
            if (!assignments.TryGetValue(instanceId, out var match))
            {
                recommendations.Add(CreateCurrentRecommendation(device));
                continue;
            }

            recommendations.Add(CreateMatchedRecommendation(device, match, options, warnings));
        }

        var unmatchedUpdates = candidateList
            .Where(candidate => !matchedUpdateIds.Contains(candidate.UpdateId))
            .ToList();

        if (unmatchedUpdates.Count > 0)
        {
            warnings.Add(
                $"{unmatchedUpdates.Count.ToString()} Windows Update driver package(s) could not be confidently associated with a specific device.");
        }

        return new RecommendationScanResult(
            recommendations,
            unmatchedUpdates,
            updateScan.Policy,
            warnings,
            DateTimeOffset.UtcNow);
    }

    private DeviceUpdateRecommendation CreateCurrentRecommendation(DeviceInventoryEntry device) =>
        new(
            device,
            null,
            UpdateClassification.Current,
            DriverRiskLevel.Low,
            RecommendationTerminology.CurrentSummary,
            RecommendationTerminology.CurrentExplanation,
            DateTimeOffset.UtcNow);

    private DeviceUpdateRecommendation CreateMatchedRecommendation(
        DeviceInventoryEntry device,
        DeviceUpdateMatch match,
        RecommendationOptions options,
        IList<string> warnings)
    {
        var update = match.Update;
        var context = CreateDeviceContext(device, update, options);
        var candidate = CreateDriverCandidate(device, update);
        var driverUpdate = CreateDriverUpdate(update, candidate, context);

        var classification = _sourcePolicy.ClassifyUpdate(driverUpdate, context);
        if (classification == UpdateClassification.WindowsRecommended
            && match.Score < UpdateApplicabilityMatcher.MinimumMatchScore + 2)
        {
            classification = UpdateClassification.ReviewRequired;
        }

        if (UpdateApplicabilityMatcher.IsFirmwareRelated(device, update))
        {
            classification = UpdateClassification.Restricted;
        }

        var risk = _riskPolicy.Classify(candidate, context);
        if (classification == UpdateClassification.Restricted)
        {
            risk = DriverRiskLevel.Restricted;
        }

        var summary = RecommendationTerminology.DescribeClassification(classification);
        var explanation = RecommendationTerminology.BuildExplanation(classification, update, match.Score);

        if (RecommendationTerminology.ContainsForbiddenMarketingLanguage(update.Title)
            || RecommendationTerminology.ContainsForbiddenMarketingLanguage(update.Description ?? string.Empty))
        {
            warnings.Add(
                $"Update '{update.Title}' contains marketing-style wording; Tortoise will not amplify it.");
        }

        return new DeviceUpdateRecommendation(
            device,
            update,
            classification,
            risk,
            summary,
            explanation,
            DateTimeOffset.UtcNow);
    }

    private static DeviceContext CreateDeviceContext(
        DeviceInventoryEntry device,
        WindowsUpdateCandidate update,
        RecommendationOptions options)
    {
        var identity = device.Snapshot.Identity;
        return new DeviceContext(
            identity.DeviceInstanceId,
            identity.ClassName,
            IsBootCriticalClass(identity.ClassName),
            string.Equals(
                identity.DeviceInstanceId,
                options.ActiveNetworkDeviceInstanceId,
                StringComparison.OrdinalIgnoreCase)
            || (options.ActiveNetworkDeviceInstanceId is null
                && identity.ClassName.Equals("Net", StringComparison.OrdinalIgnoreCase)),
            string.Equals(
                identity.DeviceInstanceId,
                options.ActiveDisplayDeviceInstanceId,
                StringComparison.OrdinalIgnoreCase)
            || (options.ActiveDisplayDeviceInstanceId is null
                && identity.ClassName.Equals("Display", StringComparison.OrdinalIgnoreCase)),
            UpdateApplicabilityMatcher.IsFirmwareRelated(device, update));
    }

    private static bool IsBootCriticalClass(string className) =>
        className.Equals("SCSIAdapter", StringComparison.OrdinalIgnoreCase)
        || className.Equals("HDC", StringComparison.OrdinalIgnoreCase)
        || className.Equals("Volume", StringComparison.OrdinalIgnoreCase);

    private static DriverCandidate CreateDriverCandidate(
        DeviceInventoryEntry device,
        WindowsUpdateCandidate update)
    {
        var package = new DriverPackage(
            new DriverIdentity(
                update.Title,
                update.Title,
                update.DriverManufacturer ?? "Unknown",
                update.DriverClass ?? device.Snapshot.Identity.ClassName,
                device.Snapshot.Identity.ClassGuid,
                update.DriverVersion ?? new Version(0, 0),
                update.DriverDate,
                null,
                null,
                update.UpdateId,
                update.DriverModel),
            new DriverSignature(true, true, update.DriverManufacturer, null),
            new DriverSource(DriverSourceKind.WindowsUpdate, "windows-update", "Windows Update", true));

        return new DriverCandidate(
            package,
            device.Snapshot.Identity.DeviceInstanceId,
            update.UpdateId,
            update.Revision);
    }

    private static DriverUpdate CreateDriverUpdate(
        WindowsUpdateCandidate update,
        DriverCandidate candidate,
        DeviceContext context)
    {
        var classification = update.Classification;
        if (UpdateApplicabilityMatcher.IsFirmwareRelated(
                new DeviceInventoryEntry(
                    new DeviceSnapshot(
                        new DeviceIdentity(
                            context.DeviceInstanceId,
                            null,
                            Guid.Empty,
                            context.ClassName,
                            string.Empty,
                            string.Empty,
                            [],
                            [],
                            null,
                            null,
                            null,
                            null,
                            true),
                        new DeviceHealth(DeviceHealthState.Unknown, null, null),
                        DateTimeOffset.UtcNow),
                    null),
                update))
        {
            classification = UpdateClassification.Restricted;
        }

        return new DriverUpdate(
            candidate,
            classification,
            DriverRiskLevel.Low,
            update.RestartRequired,
            update.RequiresEula,
            RecommendationTerminology.BuildExplanation(classification, update, 0));
    }
}
