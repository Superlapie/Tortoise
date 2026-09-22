using Tortoise.Contracts.Mutation;
using Tortoise.Core.Mutation;

namespace Tortoise.IntegrationTests;

public sealed class PublicBuildSafetyIntegrationTests
{
    [Fact]
    public void Public_build_never_resolves_real_mutation_capability()
    {
        if (MutationBuildPolicy.AllowsRealMutation)
        {
            return;
        }

        var environments = new[]
        {
            new ExecutionEnvironmentInfo(false, false, false),
            new ExecutionEnvironmentInfo(true, true, true),
            new ExecutionEnvironmentInfo(false, true, true, true),
        };

        foreach (var environment in environments)
        {
            var capability = MutationCapabilityResolver.Resolve(environment);
            Assert.False(capability.IsEnabled);
            Assert.Equal(MutationEnvironment.ReadOnly, capability.Environment);
        }
    }
}
