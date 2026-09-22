using System.Globalization;
using System.Text;
using Tortoise.Core.Drivers;

namespace Tortoise.Windows.Drivers;

internal static class PnPUtilDriverListParser
{
    internal static IReadOnlyList<DriverStorePackage> Parse(string output, bool structuredCsv)
    {
        if (structuredCsv)
        {
            return ParseCsv(output);
        }

        return ParseLegacyText(output);
    }

    internal static IReadOnlyList<DriverStorePackage> ParseCsv(string output)
    {
        var packages = new List<DriverStorePackage>();
        using var reader = new StringReader(output);
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return packages;
        }

        var headers = SplitCsvLine(headerLine);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = SplitCsvLine(line);
            if (TryParseCsvRow(headers, values, out var package))
            {
                packages.Add(package);
            }
        }

        return packages;
    }

    internal static IReadOnlyList<DriverStorePackage> ParseLegacyText(string output)
    {
        var packages = new List<DriverStorePackage>();
        var blocks = output.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            if (TryParseLegacyBlock(block, out var package))
            {
                packages.Add(package);
            }
        }

        return packages;
    }

    private static bool TryParseCsvRow(
        IReadOnlyList<string> headers,
        IReadOnlyList<string> values,
        out DriverStorePackage package)
    {
        package = null!;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count && i < values.Count; i++)
        {
            map[headers[i].Trim()] = values[i].Trim();
        }

        var publishedInf = Get(map, "Published Name", "Published Inf Name", "Driver Name");
        var originalInf = Get(map, "Original Name", "Original Inf Name", publishedInf);
        var provider = Get(map, "Provider Name", "Provider", "Signer Name");
        var className = Get(map, "Class Name", "Class");
        var classGuidText = Get(map, "Class GUID", "Class Guid");
        var versionText = Get(map, "Driver Version", "Version");
        var dateText = Get(map, "Driver Date", "Date");
        var inboxText = Get(map, "Inbox", "Boot Critical", "Is Inbox");
        var identifier = Get(map, "Extension ID", "Package Name", publishedInf);

        if (string.IsNullOrWhiteSpace(publishedInf)
            || string.IsNullOrWhiteSpace(provider)
            || !Guid.TryParse(classGuidText, out var classGuid)
            || !Version.TryParse(versionText, out var version))
        {
            return false;
        }

        package = new DriverStorePackage(
            publishedInf,
            originalInf,
            ParseBool(inboxText),
            className,
            classGuid,
            provider,
            version,
            ParseDate(dateText),
            identifier);

        return true;
    }

    private static bool TryParseLegacyBlock(string block, out DriverStorePackage package)
    {
        package = null!;
        var lines = block.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return false;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            fields[line[..separatorIndex].Trim()] = line[(separatorIndex + 1)..].Trim();
        }

        var publishedInf = Get(fields, "Published Name", "Original Name");
        var originalInf = Get(fields, "Original Name", publishedInf);
        var provider = Get(fields, "Provider Name");
        var className = Get(fields, "Class Name");
        var classGuidText = Get(fields, "Class GUID");
        var versionText = Get(fields, "Driver Version");
        var dateText = Get(fields, "Driver Date");
        var identifier = Get(fields, "Extension ID", publishedInf);

        if (string.IsNullOrWhiteSpace(publishedInf)
            || string.IsNullOrWhiteSpace(provider)
            || !Guid.TryParse(classGuidText, out var classGuid)
            || !Version.TryParse(versionText, out var version))
        {
            return false;
        }

        package = new DriverStorePackage(
            publishedInf,
            originalInf,
            ParseBool(Get(fields, "Boot Critical")),
            className,
            classGuid,
            provider,
            version,
            ParseDate(dateText),
            identifier);

        return true;
    }

    private static IReadOnlyList<string> SplitCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (character == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (character == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        values.Add(current.ToString());
        return values;
    }

    private static string Get(IReadOnlyDictionary<string, string> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static bool ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
               || value.Equals("YES", StringComparison.OrdinalIgnoreCase)
               || value.Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
        {
            return DateOnly.FromDateTime(dateTime);
        }

        return null;
    }
}
