using System.Reflection;
using System.Runtime.InteropServices;
using Tortoise.WindowsUpdate.Interop;

namespace Tortoise.WindowsUpdate.Tests;

public static class WuaComMetadataValidator
{
    public static void AssertMatchesExpectation(WuaComMetadataExpectation expectation)
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
                AssertMethodMatches(member, method!);
                continue;
            }

            var property = interfaceType.GetProperty(member.Name);
            Assert.NotNull(property);
            var propertyDispId = property!.GetCustomAttribute<DispIdAttribute>()?.Value;
            Assert.Equal(member.DispId, propertyDispId);
            AssertPropertyMatches(member, property);
        }
    }

    private static void AssertMethodMatches(WuaComMemberExpectation member, MethodInfo method)
    {
        var dispId = method.GetCustomAttribute<DispIdAttribute>()?.Value;
        Assert.Equal(member.DispId, dispId);

        if (member.ReturnType is not null)
        {
            Assert.Equal(member.ReturnType, method.ReturnType);
        }

        if (member.ParameterTypes is not null)
        {
            var parameters = method.GetParameters();
            Assert.Equal(member.ParameterTypes.Length, parameters.Length);
            for (var i = 0; i < member.ParameterTypes.Length; i++)
            {
                Assert.Equal(member.ParameterTypes[i], parameters[i].ParameterType);
            }
        }
    }

    private static void AssertPropertyMatches(WuaComMemberExpectation member, PropertyInfo property)
    {
        if (member.ReturnType is not null)
        {
            Assert.Equal(member.ReturnType, property.PropertyType);
        }

        if (member.ParameterTypes is not null)
        {
            var indexParameters = property.GetIndexParameters();
            Assert.Equal(member.ParameterTypes.Length, indexParameters.Length);
            for (var i = 0; i < member.ParameterTypes.Length; i++)
            {
                Assert.Equal(member.ParameterTypes[i], indexParameters[i].ParameterType);
            }
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

public sealed record WuaComMemberExpectation(
    string Name,
    int DispId,
    bool IsMethod = false,
    Type? ReturnType = null,
    Type[]? ParameterTypes = null);

public static class WuaComMetadataExpectations
{
    internal static IReadOnlyList<WuaComMetadataExpectation> All { get; } =
    [
        WuaComMetadataExpectation.Create<IUpdateSession>(
            "816858A4-260D-4260-933A-2585F1ABC76B",
            members:
            [
                new("ClientApplicationID", 0x60020001, ReturnType: typeof(string)),
                new("CreateUpdateSearcher", 0x60020004, IsMethod: true, ReturnType: typeof(IUpdateSearcher)),
                new("CreateUpdateDownloader", 0x60020005, IsMethod: true, ReturnType: typeof(IUpdateDownloader)),
                new("CreateUpdateInstaller", 0x60020006, IsMethod: true, ReturnType: typeof(IUpdateInstaller)),
            ]),
        WuaComMetadataExpectation.Create<IUpdateSearcher>(
            "8F45ABF1-F9AE-4B95-A933-F0F66E5056EA",
            members:
            [
                new("ServerSelection", 0x60020007, ReturnType: typeof(ServerSelection)),
                new("Search", 0x6002000C, IsMethod: true, ReturnType: typeof(ISearchResult), ParameterTypes: [typeof(string)]),
            ]),
        WuaComMetadataExpectation.Create<ISearchResult>(
            "D40CFF62-E08C-4498-941A-01E25F0FD33C",
            members:
            [
                new("ResultCode", 0x60020001, ReturnType: typeof(OperationResultCode)),
                new("Updates", 0x60020003, ReturnType: typeof(IUpdateCollection)),
            ]),
        WuaComMetadataExpectation.Create<IUpdateCollection>(
            "07F7438C-7709-4CA5-B518-91279288134E",
            members:
            [
                new("Count", 0x60020001, ReturnType: typeof(int)),
                new("Item", 0, ReturnType: typeof(IUpdate), ParameterTypes: [typeof(int)]),
                new("Add", 0x60020003, IsMethod: true, ReturnType: typeof(int), ParameterTypes: [typeof(IUpdate)]),
            ]),
        WuaComMetadataExpectation.Create<IUpdate>(
            "6A92B07A-D821-4682-B423-5C805022CC4D",
            members:
            [
                new("Title", 0, ReturnType: typeof(string)),
                new("Categories", 0x60020004, ReturnType: typeof(ICategoryCollection)),
                new("Description", 0x60020008, ReturnType: typeof(string)),
                new("EulaAccepted", 0x60020009, ReturnType: typeof(bool)),
                new("Identity", 0x6002000C, ReturnType: typeof(IUpdateIdentity)),
                new("IsHidden", 0x60020011, ReturnType: typeof(bool)),
                new("IsInstalled", 0x60020012, ReturnType: typeof(bool)),
                new("IsDownloaded", 0x6002001A, ReturnType: typeof(bool)),
                new("SupportUrl", 0x60020022, ReturnType: typeof(string)),
            ]),
        WuaComMetadataExpectation.Create<IUpdate2>(
            "144FE9B0-D23D-4A8B-8634-FB4457533B7A",
            typeof(IUpdate),
            members: [new("RebootRequired", 0x60030001, ReturnType: typeof(bool))]),
        WuaComMetadataExpectation.Create<IUpdate3>(
            "112EDA6B-95B3-476F-9D90-AEE82C6B8181",
            typeof(IUpdate2),
            members: [new("BrowseOnly", 0x60040001, ReturnType: typeof(bool))]),
        WuaComMetadataExpectation.Create<IUpdate4>(
            "27E94B0D-5139-49A2-9A61-93522DC54652",
            typeof(IUpdate3),
            members: [new("PerUser", 0x60050001, ReturnType: typeof(bool))]),
        WuaComMetadataExpectation.Create<IUpdate5>(
            "C1C2F21A-D2F4-4902-B5C6-8A081C19A890",
            typeof(IUpdate4),
            members: [new("AutoSelection", 0x60060001, ReturnType: typeof(AutoSelectionMode))]),
        WuaComMetadataExpectation.Create<IWindowsDriverUpdate>(
            "B383CD1A-5CE9-4504-9F63-764B1236F191",
            typeof(IUpdate),
            members:
            [
                new("DriverClass", 0x60030001, ReturnType: typeof(string)),
                new("DriverHardwareID", 0x60030002, ReturnType: typeof(string)),
                new("DriverManufacturer", 0x60030003, ReturnType: typeof(string)),
                new("DriverModel", 0x60030004, ReturnType: typeof(string)),
                new("DriverProvider", 0x60030005, ReturnType: typeof(string)),
                new("DriverVerDate", 0x60030006, ReturnType: typeof(DateTime)),
            ]),
        WuaComMetadataExpectation.Create<IWindowsDriverUpdate2>(
            "615C4269-7A48-43BD-96B7-BF6CA27D6C3E",
            typeof(IWindowsDriverUpdate),
            members:
            [
                new("RebootRequired", 0x60040001, ReturnType: typeof(bool)),
                new("IsPresent", 0x60040003, ReturnType: typeof(bool)),
            ]),
        WuaComMetadataExpectation.Create<IWindowsDriverUpdate3>(
            "49EBD502-4A96-41BD-9E3E-4C5057F4250C",
            typeof(IWindowsDriverUpdate2),
            members: [new("BrowseOnly", 0x60050001, ReturnType: typeof(bool))]),
        WuaComMetadataExpectation.Create<IUpdateIdentity>(
            "46297823-9940-4C09-AED9-CD3EA6D05968",
            members:
            [
                new("RevisionNumber", 0x60020002, ReturnType: typeof(int)),
                new("UpdateID", 0x60020003, ReturnType: typeof(string)),
            ]),
        WuaComMetadataExpectation.Create<ICategoryCollection>(
            "3A56BFB8-576C-43F7-9335-FE4838FD7E37",
            members:
            [
                new("Count", 0x60020001, ReturnType: typeof(int)),
                new("Item", 0, ReturnType: typeof(ICategory), ParameterTypes: [typeof(int)]),
            ]),
        WuaComMetadataExpectation.Create<ICategory>(
            "81DDC1B8-9D35-47A6-B471-5B80F519223B",
            members:
            [
                new("Name", 0, ReturnType: typeof(string)),
                new("CategoryID", 0x60020001, ReturnType: typeof(string)),
            ]),
        WuaComMetadataExpectation.Create<IUpdateDownloader>(
            "68F1C6F9-7ECC-4666-A464-247FE12496C3",
            members:
            [
                new("Updates", 0x60020004, ReturnType: typeof(IUpdateCollection)),
                new("Download", 0x60020006, IsMethod: true, ReturnType: typeof(IDownloadResult)),
            ]),
        WuaComMetadataExpectation.Create<IDownloadResult>(
            "DAA4FDD0-4727-4DBE-A1E7-745DCA317144",
            members:
            [
                new("HResult", 0x60020001, ReturnType: typeof(int)),
                new("ResultCode", 0x60020002, ReturnType: typeof(OperationResultCode)),
                new("GetUpdateResult", 0x60020003, IsMethod: true, ReturnType: typeof(IUpdateDownloadResult), ParameterTypes: [typeof(int)]),
            ]),
        WuaComMetadataExpectation.Create<IUpdateDownloadResult>(
            "BF99AF76-B575-42AD-8AA4-33CBB5477AF1",
            members:
            [
                new("HResult", 0x60020001, ReturnType: typeof(int)),
                new("ResultCode", 0x60020002, ReturnType: typeof(OperationResultCode)),
            ]),
        WuaComMetadataExpectation.Create<IUpdateInstaller>(
            "7B929C68-CCDC-4226-96B1-8724600B54C2",
            members:
            [
                new("Updates", 0x60020005, ReturnType: typeof(IUpdateCollection)),
                new("RebootRequiredBeforeInstallation", 0x6002000F, ReturnType: typeof(bool)),
                new("Install", 0x6002000A, IsMethod: true, ReturnType: typeof(IInstallationResult)),
            ]),
        WuaComMetadataExpectation.Create<IUpdateInstaller2>(
            "3442D4FE-224D-4CEE-98CF-30E0C4D229E6",
            typeof(IUpdateInstaller),
            members: [new("ForceQuiet", 0x60030001, ReturnType: typeof(bool))]),
        WuaComMetadataExpectation.Create<IInstallationResult>(
            "A43C56D6-7451-48D4-AF96-B6CD2D0D9B7A",
            members:
            [
                new("HResult", 0x60020001, ReturnType: typeof(int)),
                new("RebootRequired", 0x60020002, ReturnType: typeof(bool)),
                new("ResultCode", 0x60020003, ReturnType: typeof(OperationResultCode)),
                new("GetUpdateResult", 0x60020004, IsMethod: true, ReturnType: typeof(IUpdateInstallationResult), ParameterTypes: [typeof(int)]),
            ]),
        WuaComMetadataExpectation.Create<IUpdateInstallationResult>(
            "D940F0F8-3CBB-4FD0-993F-471E7F2328AD",
            members:
            [
                new("HResult", 0x60020001, ReturnType: typeof(int)),
                new("RebootRequired", 0x60020002, ReturnType: typeof(bool)),
                new("ResultCode", 0x60020003, ReturnType: typeof(OperationResultCode)),
            ]),
    ];

    internal static IReadOnlyList<WuaComMetadataExpectation> MutationPath { get; } =
        All.Where(expectation =>
            expectation.InterfaceType == typeof(IUpdateCollection)
            || expectation.InterfaceType == typeof(IUpdateDownloader)
            || expectation.InterfaceType == typeof(IDownloadResult)
            || expectation.InterfaceType == typeof(IUpdateInstaller)
            || expectation.InterfaceType == typeof(IInstallationResult)).ToArray();
}
