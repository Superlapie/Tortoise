namespace Tortoise.ReleaseValidation;

internal static class Program
{
    public static int Main(string[] args)
    {
        var archivePath = GetOption(args, "--archive");
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            Console.Error.WriteLine("Usage: tortoise-release-validation --archive=path/to/release.zip");
            return 1;
        }

        var result = ReleaseArchiveValidator.Validate(archivePath);
        Console.WriteLine($"Archive: {result.ArchivePath}");
        Console.WriteLine($"Entries: {result.EntryCount}");
        Console.WriteLine($"Forbidden entries found: {result.ForbiddenEntriesFound.Count}");

        foreach (var entry in result.ForbiddenEntriesFound)
        {
            Console.Error.WriteLine($"FORBIDDEN: {entry}");
        }

        return result.IsValid ? 0 : 2;
    }

    private static string? GetOption(string[] args, string name)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
            {
                return arg[(name.Length + 1)..];
            }
        }

        return null;
    }
}
