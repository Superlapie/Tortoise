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
        var result = Types.InAssembly(typeof(Tortoise.Contracts.Mutation.IMutationCapability).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Tortoise.App",
                "Tortoise.Core",
                "Tortoise.Windows",
                "Tortoise.WindowsUpdate",
                "Tortoise.Broker",
                "Tortoise.Persistence",
                "Tortoise.Security")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
