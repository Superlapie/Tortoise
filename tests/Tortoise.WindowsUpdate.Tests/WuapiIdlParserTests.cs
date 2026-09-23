namespace Tortoise.WindowsUpdate.Tests;

public sealed class WuapiIdlParserTests
{
    private const string SyntheticIdl = """
        [
            uuid(A976C28D-75A1-42AA-94AE-8AF8B872089A),
        ]
        interface IUpdateLockdown : IDispatch
        {
        };

        [
            uuid(144FE9B0-D23D-4A8B-8634-FB4457533B7A),
        ]
        interface IUpdate2 : IUpdate
        {
            HRESULT RebootRequired([out, retval] VARIANT_BOOL* retval);
        };

        [
            uuid(816858A4-260D-4260-933A-2585F1ABC76B),
            helpstring("IUpdateSession Interface"),
        ]
        interface IUpdateSession : IDispatch
        {
            HRESULT CreateUpdateSearcher([out, retval] IUpdateSearcher** retval);
        };
        """;

    [Fact]
    public void ExtractInterfaceGuid_returns_uuid_from_immediately_preceding_attribute_block()
    {
        var guid = WuapiIdlParser.ExtractInterfaceGuid(SyntheticIdl, "IUpdateSession");

        Assert.Equal("816858A4-260D-4260-933A-2585F1ABC76B", guid);
    }

    [Fact]
    public void ExtractInterfaceGuid_does_not_capture_predecessor_interface_uuid()
    {
        var update2Guid = WuapiIdlParser.ExtractInterfaceGuid(SyntheticIdl, "IUpdate2");
        var lockdownGuid = WuapiIdlParser.ExtractInterfaceGuid(SyntheticIdl, "IUpdateLockdown");

        Assert.Equal("144FE9B0-D23D-4A8B-8634-FB4457533B7A", update2Guid);
        Assert.Equal("A976C28D-75A1-42AA-94AE-8AF8B872089A", lockdownGuid);
    }

    [Fact]
    public void ExtractInterfaceGuid_fails_when_interface_is_missing()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            WuapiIdlParser.ExtractInterfaceGuid(SyntheticIdl, "IMissingInterface"));

        Assert.Contains("IMissingInterface", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractInterfaceGuid_fails_when_attribute_block_has_no_uuid()
    {
        const string idlWithoutUuid = """
            [
                helpstring("No uuid here"),
            ]
            interface IOrphan : IDispatch
            {
            };
            """;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            WuapiIdlParser.ExtractInterfaceGuid(idlWithoutUuid, "IOrphan"));

        Assert.Contains("No uuid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractInterfaceGuid_fails_when_attribute_block_has_multiple_uuids()
    {
        const string ambiguousIdl = """
            [
                uuid(11111111-1111-1111-1111-111111111111),
                uuid(22222222-2222-2222-2222-222222222222),
            ]
            interface IAmbiguous : IDispatch
            {
            };
            """;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            WuapiIdlParser.ExtractInterfaceGuid(ambiguousIdl, "IAmbiguous"));

        Assert.Contains("Ambiguous uuid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
