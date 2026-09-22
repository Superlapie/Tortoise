using Microsoft.Data.Sqlite;

namespace Tortoise.Persistence.Tests;

internal static class SqliteTestCleanup
{
    public static void ReleaseDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}
