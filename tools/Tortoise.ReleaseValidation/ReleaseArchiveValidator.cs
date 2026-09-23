using System.IO.Compression;

namespace Tortoise.ReleaseValidation;

public sealed record ReleaseArchiveValidationResult(
    string ArchivePath,
    int EntryCount,
    IReadOnlyList<string> ForbiddenEntriesFound,
    bool IsValid);

public static class ReleaseArchiveValidator
{
    public static readonly IReadOnlyList<string> ForbiddenEntryNames =
    [
        "tortoise-vm-harness",
        "tortoise-lab",
        "tortoise-lab-broker",
        "tortoise-verification-summary",
    ];

    public static readonly IReadOnlyList<string> ForbiddenEntrySuffixes =
    [
        ".Tests.dll",
        ".Tests.pdb",
    ];

    public static ReleaseArchiveValidationResult Validate(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("Release archive was not found.", archivePath);
        }

        var forbidden = new List<string>();
        var entryCount = 0;

        using (var archive = ZipFile.OpenRead(archivePath))
        {
            foreach (var entry in archive.Entries)
            {
                entryCount++;
                if (IsForbiddenEntry(entry.FullName))
                {
                    forbidden.Add(entry.FullName);
                }
            }
        }

        forbidden.Sort(StringComparer.OrdinalIgnoreCase);
        return new ReleaseArchiveValidationResult(
            Path.GetFullPath(archivePath),
            entryCount,
            forbidden,
            forbidden.Count == 0);
    }

    public static bool IsForbiddenEntry(string entryPath)
    {
        foreach (var forbiddenName in ForbiddenEntryNames)
        {
            if (entryPath.Contains(forbiddenName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var suffix in ForbiddenEntrySuffixes)
        {
            if (entryPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
