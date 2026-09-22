using System.Reflection;

namespace Tortoise.Contracts.Mutation;

/// <summary>
/// Runtime gate for real driver mutation. Public Tortoise builds never allow real mutation.
/// Only <c>tortoise-lab</c> (or explicit test harness activation) enables mutation paths.
/// </summary>
public static class MutationBuildPolicy
{
    private static bool _testLabModeEnabled;

    public const string PublicBuildNotice =
        "Read-only build — real driver mutation is structurally disabled. Use Tortoise.Lab in an isolated VM for mutation testing.";

    public const string LabBuildNotice =
        "Tortoise.Lab build — real mutation remains VM-gated and requires explicit environment markers.";

    public static bool IsLabBuild => AllowsRealMutation;

    public static bool AllowsRealMutation =>
        _testLabModeEnabled || IsLabExecutable();

    public static void EnableTestLabMode() => _testLabModeEnabled = true;

    private static bool IsLabExecutable()
    {
        var entryName = Assembly.GetEntryAssembly()?.GetName().Name;
        return string.Equals(entryName, "tortoise-lab", StringComparison.OrdinalIgnoreCase);
    }
}
