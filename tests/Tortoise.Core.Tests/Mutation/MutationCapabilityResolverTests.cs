using Tortoise.Contracts.Mutation;
using Tortoise.Core.Mutation;

namespace Tortoise.Core.Tests.Mutation;

public sealed class MutationCapabilityResolverTests
{
    [Fact]
    public void Resolve_returns_read_only_by_default()
    {
        var capability = MutationCapabilityResolver.Resolve(
            new ExecutionEnvironmentInfo(false, false, false));

        Assert.False(capability.IsEnabled);
        Assert.Equal(MutationEnvironment.ReadOnly, capability.Environment);
    }

    [Fact]
    public void Resolve_enables_disposable_vm_when_all_markers_present()
    {
        var capability = MutationCapabilityResolver.Resolve(
            new ExecutionEnvironmentInfo(true, true, true));

        Assert.True(capability.IsEnabled);
        Assert.Equal(MutationEnvironment.DisposableVm, capability.Environment);
    }

    [Fact]
    public void Resolve_requires_vm_marker_and_explicit_opt_in()
    {
        var partial = MutationCapabilityResolver.Resolve(
            new ExecutionEnvironmentInfo(true, true, false));

        Assert.False(partial.IsEnabled);
        Assert.Equal(MutationEnvironment.ReadOnly, partial.Environment);
    }
}
