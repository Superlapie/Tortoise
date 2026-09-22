namespace Tortoise.Contracts.Mutation;

public static class TortoiseLabPaths
{
    public static string LabDataRoot
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                return Path.Combine(programData, "Tortoise", "Lab");
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Tortoise", "Lab");
        }
    }

    public static string DefaultBrokerDatabasePath =>
        Path.Combine(LabDataRoot, "default", "tortoise.db");
}
