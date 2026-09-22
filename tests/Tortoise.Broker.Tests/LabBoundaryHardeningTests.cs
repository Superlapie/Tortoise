using System.Reflection;
using Tortoise.Broker.Validation;
using Tortoise.Contracts.Mutation;

namespace Tortoise.Broker.Tests;

public sealed class LabAuthoritativeStoreGuardTests
{
    [Fact]
    public void EnsureProtectedStoreReady_creates_directory_under_lab_root()
    {
        var databasePath = Path.Combine(
            TortoiseLabPaths.LabDataRoot,
            "guard-test",
            Guid.NewGuid().ToString("N"),
            "tortoise.db");

        LabAuthoritativeStoreGuard.EnsureProtectedStoreReady(databasePath);

        Assert.True(Directory.Exists(Path.GetDirectoryName(databasePath)));
    }

    [Fact]
    public void ValidateAuthoritativeStore_rejects_missing_database()
    {
        var missingPath = Path.Combine(
            TortoiseLabPaths.LabDataRoot,
            "missing",
            Guid.NewGuid().ToString("N"),
            "tortoise.db");

        var valid = LabAuthoritativeStoreGuard.ValidateAuthoritativeStore(missingPath, out var error);

        Assert.False(valid);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class MutationBuildPolicyProductionApiTests
{
    [Fact]
    public void Test_mutation_switches_are_not_public_production_api()
    {
        var publicMethods = typeof(MutationBuildPolicy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        Assert.DoesNotContain("EnableTestLabMode", publicMethods);
        Assert.DoesNotContain("EnableTestLabBrokerMode", publicMethods);
    }
}
