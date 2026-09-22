using System.Reflection;
using System.Runtime.InteropServices;
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
        var interfaceType = expectation.InterfaceType;
        var guidAttribute = interfaceType.GetCustomAttribute<GuidAttribute>();
        Assert.NotNull(guidAttribute);
        Assert.Equal(expectation.InterfaceId, guidAttribute!.Value, ignoreCase: true);

        if (expectation.DirectBaseInterface is not null)
        {
            Assert.Contains(expectation.DirectBaseInterface, interfaceType.GetInterfaces());
        }

        foreach (var member in expectation.Members)
        {
            if (member.IsMethod)
            {
                var method = interfaceType.GetMethod(member.Name);
                Assert.NotNull(method);
                var dispId = method!.GetCustomAttribute<DispIdAttribute>()?.Value;
                Assert.Equal(member.DispId, dispId);
                continue;
            }

            var property = interfaceType.GetProperty(member.Name);
            Assert.NotNull(property);
            var propertyDispId = property!.GetCustomAttribute<DispIdAttribute>()?.Value;
            Assert.Equal(member.DispId, propertyDispId);
        }
    }
}

public sealed record WuaComMetadataExpectation(
    Type InterfaceType,
    string InterfaceId,
    Type? DirectBaseInterface,
    IReadOnlyList<WuaComMemberExpectation> Members)
{
    public static WuaComMetadataExpectation Create<TInterface>(
        string interfaceId,
        Type? directBaseInterface = null,
        params WuaComMemberExpectation[] members) =>
        new(typeof(TInterface), interfaceId, directBaseInterface, members);
}

public sealed record WuaComMemberExpectation(string Name, int DispId, bool IsMethod = false);

public static class WuaComMetadataExpectations
{
    internal static IReadOnlyList<WuaComMetadataExpectation> All { get; } =
    [
        WuaComMetadataExpectation.Create<IUpdateSession>(
            "816858A4-260D-4260-933A-2585F1ABC76B",
            members:
            [
                new("ClientApplicationID", 0x60020001),
                new("CreateUpdateSearcher", 0x60020004, IsMethod: true),
                new("CreateUpdateDownloader", 0x60020005, IsMethod: true),
                new("CreateUpdateInstaller", 0x60020006, IsMethod: true),
            ]),
        WuaComMetadataExpectation.Create<IUpdateSearcher>(
            "8F45ABF1-F9AE-4B95-A933-F0F66E5056EA",
            members:
            [
                new("ServerSelection", 0x60020007),
                new("Search", 0x6002000C, IsMethod: true),
            ]),
        WuaComMetadataExpectation.Create<ISearchResult>(
            "D40CFF62-E08C-4498-941A-01E25F0FD33C",
            members:
            [
                new("ResultCode", 0x60020001),
                new("Updates", 0x60020003),
            ]),
        WuaComMetadataExpectation.Create<IUpdateCollection>(
            "07F7438C-7709-4CA5-B518-91279288134E",
            members:
            [
                new("Count", 0x60020001),
                new("Item", 0),
                new("Add", 0x60020003, IsMethod: true),
            ]),
        WuaComMetadataExpectation.Create<IUpdate>(
            "6A92B07A-D821-4682-B423-5C805022CC4D",
            members:
            [
                new("Title", 0),
                new("Categories", 0x60020004),
                new("Description", 0x60020008),
                new("EulaAccepted", 0x60020009),
                new("Identity", 0x6002000C),
                new("IsHidden", 0x60020011),
                new("IsInstalled", 0x60020012),
                new("IsDownloaded", 0x6002001A),
                new("SupportUrl", 0x60020022),
            ]),
        WuaComMetadataExpectation.Create<IUpdate2>(
            "144FE9B0-D23D-4A8B-8634-FB4457533B7A",
            typeof(IUpdate),
            members: [new("RebootRequired", 0x60030001)]),
        WuaComMetadataExpectation.Create<IUpdate3>(
            "112EDA6B-95B3-476F-9D90-AEE82C6B8181",
            typeof(IUpdate2),
            members: [new("BrowseOnly", 0x60040001)]),
        WuaComMetadataExpectation.Create<IUpdate4>(
            "27E94B0D-5139-49A2-9A61-93522DC54652",
            typeof(IUpdate3),
            members: [new("PerUser", 0x60050001)]),
        WuaComMetadataExpectation.Create<IUpdate5>(
            "C1C2F21A-D2F4-4902-B5C6-8A081C19A890",
            typeof(IUpdate4),
            members: [new("AutoSelection", 0x60060001)]),
        WuaComMetadataExpectation.Create<IWindowsDriverUpdate>(
            "B383CD1A-5CE9-4504-9F63-764B1236F191",
            typeof(IUpdate),
            members:
            [
                new("DriverClass", 0x60030001),
                new("DriverHardwareID", 0x60030002),
                new("DriverManufacturer", 0x60030003),
                new("DriverModel", 0x60030004),
                new("DriverProvider", 0x60030005),
                new("DriverVerDate", 0x60030006),
            ]),
        WuaComMetadataExpectation.Create<IWindowsDriverUpdate2>(
            "615C4269-7A48-43BD-96B7-BF6CA27D6C3E",
            typeof(IWindowsDriverUpdate),
            members:
            [
                new("RebootRequired", 0x60040001),
                new("IsPresent", 0x60040003),
            ]),
        WuaComMetadataExpectation.Create<IWindowsDriverUpdate3>(
            "49EBD502-4A96-41BD-9E3E-4C5057F4250C",
            typeof(IWindowsDriverUpdate2),
            members: [new("BrowseOnly", 0x60050001)]),
        WuaComMetadataExpectation.Create<IUpdateIdentity>(
            "46297823-9940-4C09-AED9-CD3EA6D05968",
            members:
            [
                new("RevisionNumber", 0x60020002),
                new("UpdateID", 0x60020003),
            ]),
        WuaComMetadataExpectation.Create<ICategoryCollection>(
            "3A56BFB8-576C-43F7-9335-FE4838FD7E37",
            members:
            [
                new("Count", 0x60020001),
                new("Item", 0),
            ]),
        WuaComMetadataExpectation.Create<ICategory>(
            "81DDC1B8-9D35-47A6-B471-5B80F519223B",
            members:
            [
                new("Name", 0),
                new("CategoryID", 0x60020001),
            ]),
        WuaComMetadataExpectation.Create<IUpdateDownloader>(
            "68F1C6F9-7ECC-4666-A464-247FE12496C3",
            members: [new("Updates", 0x60020004)]),
        WuaComMetadataExpectation.Create<IDownloadResult>(
            "DAA4FDD0-4727-4DBE-A1E7-745DCA317144",
            members:
            [
                new("HResult", 0x60020001),
                new("ResultCode", 0x60020002),
            ]),
        WuaComMetadataExpectation.Create<IUpdateDownloadResult>(
            "BF99AF76-B575-42AD-8AA4-33CBB5477AF1",
            members:
            [
                new("HResult", 0x60020001),
                new("ResultCode", 0x60020002),
            ]),
        WuaComMetadataExpectation.Create<IUpdateInstaller>(
            "7B929C68-CCDC-4226-96B1-8724600B54C2",
            members:
            [
                new("Updates", 0x60020005),
                new("RebootRequiredBeforeInstallation", 0x6002000F),
            ]),
        WuaComMetadataExpectation.Create<IUpdateInstaller2>(
            "3442D4FE-224D-4CEE-98CF-30E0C4D229E6",
            typeof(IUpdateInstaller),
            members: [new("ForceQuiet", 0x60030001)]),
        WuaComMetadataExpectation.Create<IInstallationResult>(
            "A43C56D6-7451-48D4-AF96-B6CD2D0D9B7A",
            members:
            [
                new("HResult", 0x60020001),
                new("RebootRequired", 0x60020002),
                new("ResultCode", 0x60020003),
            ]),
        WuaComMetadataExpectation.Create<IUpdateInstallationResult>(
            "D940F0F8-3CBB-4FD0-993F-471E7F2328AD",
            members:
            [
                new("HResult", 0x60020001),
                new("RebootRequired", 0x60020002),
                new("ResultCode", 0x60020003),
            ]),
    ];
}
