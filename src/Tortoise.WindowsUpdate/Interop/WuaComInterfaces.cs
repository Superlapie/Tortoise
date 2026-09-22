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
[Guid("4CB43D7F-7EEE-4906-8698-60DA1C38F2FE")]
[CoClass(typeof(UpdateSessionClass))]
internal interface UpdateSession : IUpdateSession
{
}

[ComImport]
[Guid("4CB43D7F-7EEE-4906-8698-60DA1C38F2FE")]
internal class UpdateSessionClass
{
}

[ComImport]
[Guid("816858A4-260D-4260-933A-2585F1ABC76B")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateSession
{
    [DispId(0x60020001)]
    string ClientApplicationID { get; set; }

    [return: MarshalAs(UnmanagedType.IDispatch)]
    object CreateUpdateSearcher();

    [return: MarshalAs(UnmanagedType.IDispatch)]
    object CreateUpdateInstaller();
}

[ComImport]
[Guid("8F45ABF1-F9AE-4B95-A933-F0F66E5056EA")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateSearcher
{
    [DispId(0x60020007)]
    ServerSelection ServerSelection { get; set; }

    [return: MarshalAs(UnmanagedType.Interface)]
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
[Guid("13639463-00DB-4646-803D-528026140D88")]
[CoClass(typeof(UpdateCollectionClass))]
internal interface UpdateCollection : IUpdateCollection
{
}

[ComImport]
[Guid("13639463-00DB-4646-803D-528026140D88")]
internal class UpdateCollectionClass
{
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
[Guid("C1C2F21A-D2F4-4902-B5C6-8A081C19A890")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdate5 : IUpdate2
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
[Guid("3CABF931-8951-4504-9037-DE4B7B5362A4")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ICategoryCollection
{
    [DispId(0x60020001)]
    int Count { get; }

    [DispId(0)]
    ICategory this[[In] int index] { get; }
}

[ComImport]
[Guid("BFA6E560-FC09-4529-8859-006A43BA45CA")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ICategory
{
    [DispId(0x60020001)]
    string Name { get; }

    [DispId(0x60020002)]
    string CategoryID { get; }
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
}
