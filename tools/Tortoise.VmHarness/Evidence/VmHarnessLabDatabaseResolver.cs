using Tortoise.Contracts.Mutation;

namespace Tortoise.VmHarness.Evidence;

public static class VmHarnessLabDatabaseResolver
{
    public static string ResolveForRun(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return Path.Combine(TortoiseLabPaths.LabDataRoot, "vm-harness", runId, "tortoise.db");
    }

    public static string ToGuestDbArgument(string databasePath) =>
        $"--db=\"{databasePath.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
