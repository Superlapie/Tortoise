using System.Runtime.Versioning;
using Tortoise.Contracts.Mutation;

namespace Tortoise.Broker.Validation;

public static class BrokerDatabasePathValidator
{
    public static bool TryValidate(string? databasePath, out string? normalizedPath, out string? error)
    {
        normalizedPath = null;
        error = null;

        var labRoot = Path.GetFullPath(TortoiseLabPaths.LabDataRoot);
        var candidatePath = string.IsNullOrWhiteSpace(databasePath)
            ? TortoiseLabPaths.DefaultBrokerDatabasePath
            : databasePath;

        if (candidatePath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            error = "Broker database path cannot be a UNC path.";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(candidatePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Broker database path is invalid: {ex.Message}";
            return false;
        }

        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            error = "Broker database path cannot resolve to a UNC path.";
            return false;
        }

        if (!IsUnderDirectory(labRoot, fullPath))
        {
            error = $"Broker database path must be located under '{labRoot}'.";
            return false;
        }

        if (OperatingSystem.IsWindows() && ContainsReparsePoint(fullPath))
        {
            error = "Broker database path cannot traverse a reparse point.";
            return false;
        }

        normalizedPath = fullPath;
        return true;
    }

    private static bool IsUnderDirectory(string rootDirectory, string candidatePath)
    {
        var normalizedRoot = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidatePath);
        return normalizedCandidate.StartsWith(normalizedRoot, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }

    [SupportedOSPlatform("windows")]
    private static bool ContainsReparsePoint(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        var relativePath = fullPath[root.Length..];
        var current = root.TrimEnd(Path.DirectorySeparatorChar);

        foreach (var segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                continue;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }
}
