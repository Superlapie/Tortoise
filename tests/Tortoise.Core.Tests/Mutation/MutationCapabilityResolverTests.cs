using Tortoise.Contracts.Mutation;
using Tortoise.Core.Mutation;

namespace Tortoise.Core.Tests.Mutation;

public sealed class MutationCapabilityResolverTests
{
    [Fact]
    public void Resolve_returns_read_only_in_public_build()
    {
        if (MutationBuildPolicy.AllowsRealMutation)
        {
            return;
        }

        var capability = MutationCapabilityResolver.Resolve(
            new ExecutionEnvironmentInfo(true, true, true));

        Assert.False(capability.IsEnabled);
        Assert.Equal(MutationEnvironment.ReadOnly, capability.Environment);
    }

    [Fact]
    public void Resolve_enables_disposable_vm_when_lab_build_and_markers_present()
    {
        if (!MutationBuildPolicy.AllowsRealMutation)
        {
            return;
        }

        var capability = MutationCapabilityResolver.Resolve(
            new ExecutionEnvironmentInfo(true, true, true));

        Assert.True(capability.IsEnabled);
        Assert.Equal(MutationEnvironment.DisposableVm, capability.Environment);
    }

    [Fact]
    public void Resolve_requires_detected_vm_and_explicit_opt_in_in_lab_build()
    {
        if (!MutationBuildPolicy.AllowsRealMutation)
        {
            return;
        }

        var partial = MutationCapabilityResolver.Resolve(
            new ExecutionEnvironmentInfo(false, true, true));

        Assert.False(partial.IsEnabled);
        Assert.Equal(MutationEnvironment.ReadOnly, partial.Environment);
    }
}
