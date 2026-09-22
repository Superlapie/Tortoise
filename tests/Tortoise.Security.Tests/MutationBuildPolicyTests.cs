using Tortoise.Contracts.Mutation;
using Tortoise.Security.Broker;

namespace Tortoise.Security.Tests;

public sealed class MutationBuildPolicyTests
{
    [Fact]
    public void Public_build_notice_is_present()
    {
        Assert.Contains("runtime-guarded", MutationBuildPolicy.PublicBuildNotice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Default_build_disallows_real_mutation()
    {
        Assert.False(MutationBuildPolicy.AllowsRealMutation);
        Assert.False(MutationBuildPolicy.IsLabBuild);
        Assert.Equal(MutationProcessRole.Public, MutationBuildPolicy.ResolveRole("tortoise"));
    }

    [Theory]
    [InlineData("tortoise-lab", MutationProcessRole.LabClient)]
    [InlineData("tortoise-lab-broker", MutationProcessRole.LabBroker)]
    [InlineData("tortoise", MutationProcessRole.Public)]
    [InlineData("Tortoise", MutationProcessRole.Public)]
    public void ResolveRole_maps_entry_assemblies(string entryAssemblyName, MutationProcessRole expectedRole)
    {
        Assert.Equal(expectedRole, MutationBuildPolicy.ResolveRole(entryAssemblyName));
    }

    [Theory]
    [InlineData(MutationProcessRole.LabClient, true)]
    [InlineData(MutationProcessRole.LabBroker, true)]
    [InlineData(MutationProcessRole.Test, true)]
    [InlineData(MutationProcessRole.Public, false)]
    public void AllowsMutationCapabilityResolutionForRole_matches_process_role(
        MutationProcessRole role,
        bool expected)
    {
        Assert.Equal(expected, MutationBuildPolicy.AllowsMutationCapabilityResolutionForRole(role));
    }

    [Fact]
    public void Lab_broker_entry_identity_is_eligible_for_mutation_capability_resolution()
    {
        var role = MutationBuildPolicy.ResolveRole("tortoise-lab-broker");

        Assert.Equal(MutationProcessRole.LabBroker, role);
        Assert.True(MutationBuildPolicy.AllowsMutationCapabilityResolutionForRole(role));
    }
}

public sealed class BrokerRequestValidatorSecurityTests
{
    [Fact]
    public void Validate_rejects_invalid_capability_token()
    {
        var validator = new BrokerRequestValidator("expected-token");
        var replayGuard = new BrokerReplayGuard();
        var request = BrokerRequestFactory.Create(
            Tortoise.Contracts.Elevation.BrokerOperation.Ping,
            sessionId: 1,
            capabilityToken: "wrong-token");

        var result = validator.Validate(request, expectedSessionId: 1, replayGuard);

        Assert.False(result.IsValid);
        Assert.Equal(Tortoise.Contracts.Elevation.BrokerErrorCode.UnauthorizedClient, result.ErrorCode);
    }
}
