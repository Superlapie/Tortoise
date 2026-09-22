using NetArchTest.Rules;

namespace Tortoise.ArchitectureTests;

public sealed class LayerDependencyTests
{
    [Fact]
    public void Core_must_not_reference_app_or_windows_layers()
    {
        var result = Types.InAssembly(typeof(Tortoise.Core.Mutation.MutationGuard).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Tortoise.App",
                "Tortoise.Windows",
                "Tortoise.WindowsUpdate",
                "Tortoise.Broker",
                "Tortoise.Persistence")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Contracts_must_not_reference_implementation_layers()
    {
        var assembly = typeof(Tortoise.Contracts.Mutation.IMutationCapability).Assembly;
        var forbidden =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Tortoise.App",
                "Tortoise.Core",
                "Tortoise.Windows",
                "Tortoise.WindowsUpdate",
                "Tortoise.Broker",
                "Tortoise.Persistence",
                "Tortoise.Security",
            };

        var violations = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && forbidden.Contains(name))
            .ToList();

        Assert.True(violations.Count == 0, string.Join(", ", violations));
    }
}
