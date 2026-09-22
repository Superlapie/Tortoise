namespace Tortoise.Contracts.Mutation;

public static class TortoiseEnvironmentVariables
{
    public const string MutationTests = "TORTOISE_MUTATION_TESTS";

    public const string AllowVmInstall = "TORTOISE_ALLOW_VM_INSTALL";

    public const string VmMarker = "TORTOISE_VM_MARKER";

    public static bool IsTruthy(string? value) =>
        value is "1" or "true" or "TRUE" or "yes" or "YES";
}
