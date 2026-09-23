using Tortoise.WindowsUpdate.Interop;

namespace Tortoise.WindowsUpdate.Tests;

public sealed class WuaComMetadataTests
{
    public static IEnumerable<object[]> InterfaceMetadata =>
        WuaComMetadataExpectations.All.Select(expectation => new object[] { expectation });

    [Theory]
    [MemberData(nameof(InterfaceMetadata))]
    public void Managed_interface_matches_microsoft_wuapi_idl(WuaComMetadataExpectation expectation)
    {
        WuaComMetadataValidator.AssertMatchesExpectation(expectation);
    }
}

public sealed class WuaComMutationPathSignatureTests
{
    public static IEnumerable<object[]> MutationPathMetadata =>
        WuaComMetadataExpectations.MutationPath.Select(expectation => new object[] { expectation });

    [Theory]
    [MemberData(nameof(MutationPathMetadata))]
    public void Mutation_path_interface_matches_wuapi_method_signatures(WuaComMetadataExpectation expectation)
    {
        WuaComMetadataValidator.AssertMatchesExpectation(expectation);
    }
}
