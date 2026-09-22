using System.Runtime.InteropServices;

namespace Tortoise.WindowsUpdate.Interop;

internal enum ServerSelection
{
    Default = 0,
    ManagedServer = 1,
    WindowsUpdate = 2,
    Others = 3,
}

internal enum OperationResultCode
{
    NotStarted = 0,
    InProgress = 1,
    Succeeded = 2,
    SucceededWithErrors = 3,
    Failed = 4,
    Aborted = 5,
}

internal enum AutoSelectionMode
{
    LetWindowsUpdateDecide = 0,
    AutoSelectIfDownloaded = 1,
    NeverAutoSelect = 2,
    AlwaysAutoSelect = 3,
}

[ComImport]
[Guid("816858A4-260D-4260-933A-2585F1ABC76B")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateSession
{
    [DispId(0x60020001)]
    string ClientApplicationID { get; set; }

    [DispId(0x60020004)]
    IUpdateSearcher CreateUpdateSearcher();

    [DispId(0x60020005)]
    IUpdateDownloader CreateUpdateDownloader();

    [DispId(0x60020006)]
    IUpdateInstaller CreateUpdateInstaller();
}

[ComImport]
[Guid("8F45ABF1-F9AE-4B95-A933-F0F66E5056EA")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateSearcher
{
    [DispId(0x60020007)]
    ServerSelection ServerSelection { get; set; }

    [DispId(0x6002000C)]
    ISearchResult Search([In][MarshalAs(UnmanagedType.BStr)] string criteria);
}

[ComImport]
[Guid("D40CFF62-E08C-4498-941A-01E25F0FD33C")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ISearchResult
{
    [DispId(0x60020001)]
    OperationResultCode ResultCode { get; }

    [DispId(0x60020003)]
    IUpdateCollection Updates { get; }
}

[ComImport]
[Guid("07F7438C-7709-4CA5-B518-91279288134E")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateCollection
{
    [DispId(0x60020001)]
    int Count { get; }

    [DispId(0)]
    IUpdate this[[In] int index] { get; }

    [DispId(0x60020003)]
    void Add([In] IUpdate update);
}

[ComImport]
[Guid("6A92B07A-D821-4682-B423-5C805022CC4D")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdate
{
    [DispId(0)]
    string Title { get; }

    [DispId(0x60020004)]
    ICategoryCollection Categories { get; }

    [DispId(0x60020008)]
    string Description { get; }

    [DispId(0x60020009)]
    bool EulaAccepted { get; }

    [DispId(0x6002000C)]
    IUpdateIdentity Identity { get; }

    [DispId(0x60020011)]
    bool IsHidden { get; }

    [DispId(0x60020012)]
    bool IsInstalled { get; }

    [DispId(0x6002001A)]
    bool IsDownloaded { get; }

    [DispId(0x60020022)]
    string SupportUrl { get; }
}

[ComImport]
[Guid("144FE9B0-D23D-4A8B-8634-FB4457533B7A")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdate2 : IUpdate
{
    [DispId(0x60030001)]
    bool RebootRequired { get; }
}

[ComImport]
[Guid("112EDA6B-95B3-476F-9D90-AEE82C6B8181")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdate3 : IUpdate2
{
    [DispId(0x60040001)]
    bool BrowseOnly { get; }
}

[ComImport]
[Guid("27E94B0D-5139-49A2-9A61-93522DC54652")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdate4 : IUpdate3
{
    [DispId(0x60050001)]
    bool PerUser { get; }
}

[ComImport]
[Guid("C1C2F21A-D2F4-4902-B5C6-8A081C19A890")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdate5 : IUpdate4
{
    [DispId(0x60060001)]
    AutoSelectionMode AutoSelection { get; }
}

[ComImport]
[Guid("B383CD1A-5CE9-4504-9F63-764B1236F191")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IWindowsDriverUpdate : IUpdate
{
    [DispId(0x60030001)]
    string DriverClass { get; }

    [DispId(0x60030002)]
    string DriverHardwareID { get; }

    [DispId(0x60030003)]
    string DriverManufacturer { get; }

    [DispId(0x60030004)]
    string DriverModel { get; }

    [DispId(0x60030005)]
    string DriverProvider { get; }

    [DispId(0x60030006)]
    DateTime DriverVerDate { get; }
}

[ComImport]
[Guid("615C4269-7A48-43BD-96B7-BF6CA27D6C3E")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IWindowsDriverUpdate2 : IWindowsDriverUpdate
{
    [DispId(0x60040001)]
    bool RebootRequired { get; }

    [DispId(0x60040003)]
    bool IsPresent { get; }
}

[ComImport]
[Guid("49EBD502-4A96-41BD-9E3E-4C5057F4250C")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IWindowsDriverUpdate3 : IWindowsDriverUpdate2
{
    [DispId(0x60050001)]
    bool BrowseOnly { get; }
}

[ComImport]
[Guid("46297823-9940-4C09-AED9-CD3EA6D05968")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateIdentity
{
    [DispId(0x60020002)]
    int RevisionNumber { get; }

    [DispId(0x60020003)]
    string UpdateID { get; }
}

[ComImport]
[Guid("3A56BFB8-576C-43F7-9335-FE4838FD7E37")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ICategoryCollection
{
    [DispId(0x60020001)]
    int Count { get; }

    [DispId(0)]
    ICategory this[[In] int index] { get; }
}

[ComImport]
[Guid("81DDC1B8-9D35-47A6-B471-5B80F519223B")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ICategory
{
    [DispId(0)]
    string Name { get; }

    [DispId(0x60020001)]
    string CategoryID { get; }
}

[ComImport]
[Guid("68F1C6F9-7ECC-4666-A464-247FE12496C3")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateDownloader
{
    [DispId(0x60020004)]
    IUpdateCollection Updates { get; set; }

    [return: MarshalAs(UnmanagedType.Interface)]
    IDownloadResult Download();
}

[ComImport]
[Guid("DAA4FDD0-4727-4DBE-A1E7-745DCA317144")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IDownloadResult
{
    [DispId(0x60020001)]
    int HResult { get; }

    [DispId(0x60020002)]
    OperationResultCode ResultCode { get; }

    [DispId(0x60020003)]
    IUpdateDownloadResult GetUpdateResult([In] int index);
}

[ComImport]
[Guid("BF99AF76-B575-42AD-8AA4-33CBB5477AF1")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateDownloadResult
{
    [DispId(0x60020001)]
    int HResult { get; }

    [DispId(0x60020002)]
    OperationResultCode ResultCode { get; }
}

[ComImport]
[Guid("7B929C68-CCDC-4226-96B1-8724600B54C2")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateInstaller
{
    [DispId(0x60020005)]
    IUpdateCollection Updates { get; set; }

    [DispId(0x6002000F)]
    bool RebootRequiredBeforeInstallation { get; }

    [return: MarshalAs(UnmanagedType.Interface)]
    IInstallationResult Install();
}

[ComImport]
[Guid("3442D4FE-224D-4CEE-98CF-30E0C4D229E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateInstaller2 : IUpdateInstaller
{
    [DispId(0x60030001)]
    bool ForceQuiet { get; set; }
}

[ComImport]
[Guid("A43C56D6-7451-48D4-AF96-B6CD2D0D9B7A")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IInstallationResult
{
    [DispId(0x60020001)]
    int HResult { get; }

    [DispId(0x60020002)]
    bool RebootRequired { get; }

    [DispId(0x60020003)]
    OperationResultCode ResultCode { get; }

    [DispId(0x60020004)]
    IUpdateInstallationResult GetUpdateResult([In] int index);
}

[ComImport]
[Guid("D940F0F8-3CBB-4FD0-993F-471E7F2328AD")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateInstallationResult
{
    [DispId(0x60020001)]
    int HResult { get; }

    [DispId(0x60020002)]
    bool RebootRequired { get; }

    [DispId(0x60020003)]
    OperationResultCode ResultCode { get; }
}
