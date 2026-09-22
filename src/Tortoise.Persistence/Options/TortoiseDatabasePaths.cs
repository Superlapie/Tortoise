namespace Tortoise.Persistence.Options;

public static class TortoiseDatabasePaths
{
    public static string GetDefaultDatabasePath()
    {
        if (OperatingSystem.IsWindows())
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, "Tortoise", "tortoise.db");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Tortoise", "tortoise.db");
    }
}
