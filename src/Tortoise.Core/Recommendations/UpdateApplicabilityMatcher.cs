using Tortoise.Core.Devices;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Recommendations;

internal static class UpdateApplicabilityMatcher
{
    internal const int MinimumMatchScore = 4;

    internal static int Score(DeviceInventoryEntry device, WindowsUpdateCandidate update)
    {
        var identity = device.Snapshot.Identity;
        var score = 0;

        if (ClassMatches(identity.ClassName, update))
        {
            score += 2;
        }

        if (ManufacturerMatches(identity.Manufacturer, device.InstalledDriver?.ProviderName, update.DriverManufacturer))
        {
            score += 2;
        }

        if (HardwareIdMatches(identity.HardwareIds, update.Title, update.Description))
        {
            score += 6;
        }

        if (ModelMatches(update.DriverModel, identity.FriendlyName, update.Title))
        {
            score += 2;
        }

        if (InfMatches(device.InstalledDriver?.InfName, update.Title, update.Description))
        {
            score += 1;
        }

        return score;
    }

    internal static bool HasStrongMatch(DeviceInventoryEntry device, WindowsUpdateCandidate update, int score)
    {
        if (score < MinimumMatchScore)
        {
            return false;
        }

        if (HardwareIdMatches(device.Snapshot.Identity.HardwareIds, update.Title, update.Description))
        {
            return true;
        }

        if (IsFirmwareRelated(device, update) && score >= MinimumMatchScore)
        {
            return true;
        }

        return score >= MinimumMatchScore + 4;
    }

    internal static bool IsFirmwareRelated(DeviceInventoryEntry device, WindowsUpdateCandidate update)
    {
        var identity = device.Snapshot.Identity;
        if (identity.ClassName.Contains("firmware", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (update.Categories.Any(category =>
                category.Contains("firmware", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return ContainsAny(update.Title, "firmware", "uefi", "bios")
               || ContainsAny(update.Description, "firmware", "uefi", "bios");
    }

    private static bool ClassMatches(string deviceClass, WindowsUpdateCandidate update)
    {
        if (string.IsNullOrWhiteSpace(deviceClass))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(update.DriverClass)
            && string.Equals(deviceClass, update.DriverClass, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return update.Categories.Any(category =>
            category.Contains(deviceClass, StringComparison.OrdinalIgnoreCase)
            || deviceClass.Contains(category, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ManufacturerMatches(
        string deviceManufacturer,
        string? installedProvider,
        string? updateManufacturer)
    {
        if (string.IsNullOrWhiteSpace(updateManufacturer))
        {
            return false;
        }

        return ContainsEquivalentManufacturer(deviceManufacturer, updateManufacturer)
               || (installedProvider is not null
                   && ContainsEquivalentManufacturer(installedProvider, updateManufacturer));
    }

    private static bool ContainsEquivalentManufacturer(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return left.Contains(right, StringComparison.OrdinalIgnoreCase)
               || right.Contains(left, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HardwareIdMatches(
        IReadOnlyList<string> hardwareIds,
        string? title,
        string? description)
    {
        if (hardwareIds.Count == 0)
        {
            return false;
        }

        var haystack = $"{title}\n{description}";
        if (string.IsNullOrWhiteSpace(haystack))
        {
            return false;
        }

        return hardwareIds.Any(id =>
            !string.IsNullOrWhiteSpace(id)
            && haystack.Contains(id, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ModelMatches(string? driverModel, string friendlyName, string title)
    {
        if (string.IsNullOrWhiteSpace(driverModel))
        {
            return false;
        }

        return friendlyName.Contains(driverModel, StringComparison.OrdinalIgnoreCase)
               || title.Contains(driverModel, StringComparison.OrdinalIgnoreCase);
    }

    private static bool InfMatches(string? infName, string? title, string? description)
    {
        if (string.IsNullOrWhiteSpace(infName))
        {
            return false;
        }

        var normalized = infName.Trim().TrimStart('@');
        var haystack = $"{title}\n{description}";
        return haystack.Contains(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string? value, params string[] terms)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}

internal readonly record struct DeviceUpdateMatch(
    string DeviceInstanceId,
    WindowsUpdateCandidate Update,
    int Score);

internal static class UpdateAssignmentPlanner
{
    internal static IReadOnlyDictionary<string, DeviceUpdateMatch> AssignBestMatches(
        IReadOnlyList<DeviceInventoryEntry> devices,
        IReadOnlyList<WindowsUpdateCandidate> updates)
    {
        var pairs = new List<(string DeviceInstanceId, WindowsUpdateCandidate Update, int Score)>();

        foreach (var device in devices)
        {
            foreach (var update in updates)
            {
                var score = UpdateApplicabilityMatcher.Score(device, update);
                if (UpdateApplicabilityMatcher.HasStrongMatch(device, update, score))
                {
                    pairs.Add((device.Snapshot.Identity.DeviceInstanceId, update, score));
                }
            }
        }

        pairs.Sort((left, right) => right.Score.CompareTo(left.Score));

        var assignedDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assignedUpdates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, DeviceUpdateMatch>(StringComparer.OrdinalIgnoreCase);

        foreach (var (deviceInstanceId, update, score) in pairs)
        {
            if (assignedDevices.Contains(deviceInstanceId) || assignedUpdates.Contains(update.UpdateId))
            {
                continue;
            }

            assignedDevices.Add(deviceInstanceId);
            assignedUpdates.Add(update.UpdateId);
            result[deviceInstanceId] = new DeviceUpdateMatch(deviceInstanceId, update, score);
        }

        return result;
    }
}
