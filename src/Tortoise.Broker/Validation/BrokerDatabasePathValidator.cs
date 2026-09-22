namespace Tortoise.Broker.Validation;

public static class BrokerDatabasePathValidator
{
    public static bool TryValidate(string? databasePath, out string? normalizedPath, out string? error)
    {
        normalizedPath = null;
        error = null;

        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return true;
        }

        if (databasePath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            error = "Broker database path cannot be a UNC path.";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(databasePath);
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

        normalizedPath = fullPath;
        return true;
    }
}
