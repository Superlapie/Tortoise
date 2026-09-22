namespace Tortoise.Core.Devices;

public static class DeviceProblemCodeCatalog
{
    private static readonly IReadOnlyDictionary<int, string> KnownDescriptions =
        new Dictionary<int, string>
        {
            [1] = "This device is not configured correctly.",
            [3] = "The driver for this device might be corrupted, or your system may be running low on memory or other resources.",
            [6] = "Another device is using the resources this device needs.",
            [9] = "Windows cannot identify all the resources this device uses.",
            [10] = "This device cannot start.",
            [12] = "This device cannot find enough free resources that it can use.",
            [14] = "This device cannot work properly until you restart the computer.",
            [18] = "Reinstall the drivers for this device.",
            [21] = "Windows is removing this device.",
            [22] = "No drivers are installed for this device.",
            [24] = "This device is not present, is not working properly, or does not have all its drivers installed.",
            [28] = "The drivers for this device are not installed.",
            [31] = "This device is not working properly because Windows cannot load the drivers required for this device.",
            [43] = "Windows has stopped this device because it has reported problems.",
        };

    public static string? TryGetDescription(int problemCode)
    {
        return KnownDescriptions.TryGetValue(problemCode, out var description)
            ? description
            : null;
    }

    public static string Describe(int? problemCode)
    {
        if (problemCode is null or 0)
        {
            return "No Windows problem code reported.";
        }

        return TryGetDescription(problemCode.Value)
               ?? $"Windows problem code: {problemCode.Value}";
    }
}
