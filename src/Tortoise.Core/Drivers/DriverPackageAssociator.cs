using Tortoise.Core.Devices;

namespace Tortoise.Core.Drivers;

public static class DriverPackageAssociator
{
    public static IReadOnlyList<DeviceDriverAssociation> Associate(
        IReadOnlyList<DeviceInventoryEntry> devices,
        IReadOnlyList<DriverStorePackage> storePackages)
    {
        var associations = new List<DeviceDriverAssociation>();

        foreach (var device in devices)
        {
            var binding = device.InstalledDriver;
            if (binding is null)
            {
                continue;
            }

            var match = FindBestMatch(binding, storePackages);
            if (match is null)
            {
                continue;
            }

            associations.Add(new DeviceDriverAssociation(
                device.Snapshot.Identity.DeviceInstanceId,
                match,
                DriverAssociationKind.Active));
        }

        return associations;
    }

    internal static DriverStorePackage? FindBestMatch(
        DeviceDriverBinding binding,
        IReadOnlyList<DriverStorePackage> storePackages)
    {
        DriverStorePackage? best = null;
        var bestScore = int.MinValue;

        foreach (var package in storePackages)
        {
            var score = Score(binding, package);
            if (score > bestScore)
            {
                bestScore = score;
                best = package;
            }
        }

        return bestScore >= 4 ? best : null;
    }

    private static int Score(DeviceDriverBinding binding, DriverStorePackage package)
    {
        var score = 0;

        if (VersionsEqual(binding.DriverVersion, package.DriverVersion))
        {
            score += 3;
        }

        if (InfNamesEquivalent(binding.InfName, package.PublishedInfName, package.OriginalInfName))
        {
            score += 3;
        }

        if (ProvidersEquivalent(binding.ProviderName, package.ProviderName))
        {
            score += 2;
        }

        if (binding.DriverDate is not null
            && package.DriverDate is not null
            && binding.DriverDate == package.DriverDate)
        {
            score += 1;
        }

        return score;
    }

    private static bool VersionsEqual(Version? left, Version right) =>
        left is not null && left == right;

    private static bool InfNamesEquivalent(string? bindingInf, string publishedInf, string originalInf)
    {
        if (string.IsNullOrWhiteSpace(bindingInf))
        {
            return false;
        }

        return InfEquals(bindingInf, publishedInf) || InfEquals(bindingInf, originalInf);
    }

    private static bool InfEquals(string left, string right) =>
        string.Equals(NormalizeInf(left), NormalizeInf(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeInf(string value) => value.Trim().TrimStart('@');

    private static bool ProvidersEquivalent(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return false;
        }

        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
