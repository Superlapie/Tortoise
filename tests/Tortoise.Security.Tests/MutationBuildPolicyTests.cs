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
