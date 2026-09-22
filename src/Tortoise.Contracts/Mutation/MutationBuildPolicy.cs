using System.Reflection;

namespace Tortoise.Contracts.Mutation;

/// <summary>
/// Runtime gate for real driver mutation. Public Tortoise builds never allow real mutation.
/// Only <c>tortoise-lab</c>, <c>tortoise-lab-broker</c>, or explicit test harness activation
/// enable mutation capability resolution.
/// </summary>
public static class MutationBuildPolicy
{
    private static bool _testLabModeEnabled;
    private static bool _testLabBrokerModeEnabled;

    public const string PublicBuildNotice =
        "Read-only build — real driver mutation is runtime-guarded in public binaries. Use Tortoise.Lab in an isolated VM for mutation testing.";

    public const string LabBuildNotice =
        "Tortoise.Lab build — real mutation remains VM-gated and requires explicit environment markers.";

    public static MutationProcessRole CurrentRole =>
        ResolveRole(Assembly.GetEntryAssembly()?.GetName().Name);

    public static bool IsLabBuild =>
        CurrentRole == MutationProcessRole.LabClient || _testLabModeEnabled;

    public static bool IsLabBrokerBuild =>
        CurrentRole == MutationProcessRole.LabBroker || _testLabBrokerModeEnabled;

    public static bool AllowsMutationCapabilityResolution =>
        CurrentRole is MutationProcessRole.LabClient
            or MutationProcessRole.LabBroker
            or MutationProcessRole.Test;

    public static bool AllowsRealMutation => AllowsMutationCapabilityResolution;

    internal static void EnableTestLabMode() => _testLabModeEnabled = true;

    internal static void EnableTestLabBrokerMode() => _testLabBrokerModeEnabled = true;

    public static MutationProcessRole ResolveRole(string? entryAssemblyName)
    {
        if (_testLabModeEnabled || _testLabBrokerModeEnabled)
        {
            return MutationProcessRole.Test;
        }

        if (string.Equals(entryAssemblyName, "tortoise-lab", StringComparison.OrdinalIgnoreCase))
        {
            return MutationProcessRole.LabClient;
        }

        if (string.Equals(entryAssemblyName, "tortoise-lab-broker", StringComparison.OrdinalIgnoreCase))
        {
            return MutationProcessRole.LabBroker;
        }

        return MutationProcessRole.Public;
    }

    public static bool AllowsMutationCapabilityResolutionForRole(MutationProcessRole role) =>
        role is MutationProcessRole.LabClient
            or MutationProcessRole.LabBroker
            or MutationProcessRole.Test;
}
