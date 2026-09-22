using Tortoise.Contracts.Mutation;
using Tortoise.Core.Mutation;

namespace Tortoise.Core.Tests.Mutation;

public sealed class MutationGuardTests
{
    [Fact]
    public void RequireMutationEnabled_throws_when_mutation_disabled()
    {
        var capability = MutationCapability.ReadOnly;

        var exception = Assert.Throws<MutationDeniedException>(() =>
            MutationGuard.RequireMutationEnabled(capability, "install-driver"));

        Assert.Contains("install-driver", exception.Message, StringComparison.Ordinal);
        Assert.Contains(capability.Reason, exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireMutationEnabled_allows_when_mutation_enabled()
    {
        var capability = new MutationCapability(
            isEnabled: true,
            environment: MutationEnvironment.Simulated,
            reason: "Simulated backend enabled for tests.");

        MutationGuard.RequireMutationEnabled(capability, "simulated-install");
    }
}
