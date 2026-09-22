using System.Text.RegularExpressions;

namespace Tortoise.Persistence.Diagnostics;

internal static partial class RedactionHelper
{
    [GeneratedRegex(@"(?i)(\\users\\)[^\\]+", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsUserProfilePathRegex();

    [GeneratedRegex(@"(?i)(/home/)[^/]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnixHomePathRegex();

    [GeneratedRegex(@"(?i)\b(?:CN|DC|HOST|COMPUTER)=([^,;\s]+)", RegexOptions.CultureInvariant)]
    private static partial Regex DistinguishedNameHostRegex();

    public static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value ?? string.Empty;
        }

        var redacted = WindowsUserProfilePathRegex().Replace(value, "$1[REDACTED]");
        redacted = UnixHomePathRegex().Replace(redacted, "$1[REDACTED]");
        redacted = DistinguishedNameHostRegex().Replace(redacted, "$1[REDACTED]");

        var userName = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(userName))
        {
            redacted = redacted.Replace(userName, "[REDACTED]", StringComparison.OrdinalIgnoreCase);
        }

        var machineName = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(machineName))
        {
            redacted = redacted.Replace(machineName, "[REDACTED]", StringComparison.OrdinalIgnoreCase);
        }

        return redacted;
    }
}
