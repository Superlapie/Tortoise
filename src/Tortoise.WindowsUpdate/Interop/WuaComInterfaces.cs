using System.Runtime.InteropServices;

namespace Tortoise.WindowsUpdate.Interop;

internal enum ServerSelection
{
    Default = 0,
    ManagedServer = 1,
    WindowsUpdate = 2,
    Others = 3,
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
    [DispId(1610596874)]
    string ClientApplicationID { get; set; }

    [return: MarshalAs(UnmanagedType.IDispatch)]
    object CreateUpdateSearcher();

    [return: MarshalAs(UnmanagedType.IDispatch)]
    object CreateUpdateDownloader();

    [return: MarshalAs(UnmanagedType.IDispatch)]
    object CreateUpdateInstaller();
}

[ComImport]
[Guid("562933AD-C0A5-421B-B64D-563F7F564531")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateSearcher
{
    [return: MarshalAs(UnmanagedType.IDispatch)]
    object Search([In][MarshalAs(UnmanagedType.BStr)] string criteria);

    [DispId(1610596874)]
    ServerSelection ServerSelection { get; set; }
}

[ComImport]
[Guid("136EBBC0-E36A-11D2-8716-00A0C9082637")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ISearchResult
{
    [DispId(1610743810)]
    IUpdateCollection Updates { get; }

    [DispId(1610743811)]
    int ResultCode { get; }
}

[ComImport]
[Guid("7A564898-484E-4A52-8E60-5D82ACF3A9FF")]
[CoClass(typeof(UpdateCollectionClass))]
internal interface UpdateCollection : IUpdateCollection
{
}

[ComImport]
[Guid("7A564898-484E-4A52-8E60-5D82ACF3A9FF")]
internal class UpdateCollectionClass
{
}

[ComImport]
[Guid("EA04EEE1-21A1-4565-924D-E6259DAB7274")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateCollection
{
    [DispId(1610743810)]
    int Count { get; }

    [DispId(0)]
    IUpdate this[[In][MarshalAs(UnmanagedType.Struct)] int index] { get; }

    [DispId(1610743812)]
    void Add([In][MarshalAs(UnmanagedType.IDispatch)] IUpdate update);
}

[ComImport]
[Guid("EAA92B83-22F3-4F38-B30F-1FDB84EFCC4F")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdate
{
    [DispId(1610743810)]
    IUpdateIdentity Identity { get; }

    [DispId(1610743811)]
    string Title { get; }

    [DispId(1610743812)]
    string Description { get; }

    [DispId(1610743818)]
    bool IsHidden { get; }

    [DispId(1610743819)]
    bool IsInstalled { get; }

    [DispId(1610743820)]
    bool RebootRequired { get; }

    [DispId(1610743821)]
    string DriverModel { get; }

    [DispId(1610743822)]
    string DriverManufacturer { get; }

    [DispId(1610743823)]
    ICategoryCollection Categories { get; }

    [DispId(1610743824)]
    bool EulaAccepted { get; }

    [DispId(1610743825)]
    string MoreInfoUrl { get; }

    [DispId(1610743826)]
    string SupportUrl { get; }
}

[ComImport]
[Guid("5A838388-66C7-497D-8798-99D48588471A")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdate2 : IUpdate
{
    [DispId(1610743830)]
    int AutoSelection { get; }
}

[ComImport]
[Guid("25E74697-82DB-4A09-AB3C-0CA5D5D9016B")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdate5 : IUpdate2
{
}

[ComImport]
[Guid("B49426D1-248E-4C4B-9ADC-D3B647660190")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IWindowsDriverUpdate : IUpdate2
{
    [DispId(1610743840)]
    string DriverHardwareID { get; }

    [DispId(1610743841)]
    string DriverClass { get; }

    [DispId(1610743842)]
    string DriverProvider { get; }

    [DispId(1610743843)]
    DateTime DriverVerDate { get; }

    [DispId(1610743844)]
    int DeviceProblemNumber { get; }
}

[ComImport]
[Guid("C3AB67EF-D8E3-455F-98F6-06B80F70CC7C")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IWindowsDriverUpdate5 : IWindowsDriverUpdate
{
}

[ComImport]
[Guid("67614681-9F1B-4192-93AF-0E0BD4D0C4F2")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateIdentity
{
    [DispId(1610743810)]
    string UpdateID { get; }

    [DispId(1610743811)]
    int RevisionNumber { get; }
}

[ComImport]
[Guid("3CABF931-8951-4504-9037-DE4B7B5362A4")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ICategoryCollection
{
    [DispId(1610743810)]
    int Count { get; }

    [DispId(0)]
    ICategory this[[In][MarshalAs(UnmanagedType.Struct)] int index] { get; }
}

[ComImport]
[Guid("BFA6E560-FC09-4529-8859-006A43BA45CA")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ICategory
{
    [DispId(1610743810)]
    string Name { get; }

    [DispId(1610743811)]
    string CategoryID { get; }
}

[ComImport]
[Guid("7B905F35-12DE-4CEC-ADFB-B9AED56552FA")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateInstaller
{
    [DispId(1610743810)]
    IUpdateCollection Updates { get; set; }

    [DispId(1610743811)]
    bool ForceQuiet { get; set; }

    [DispId(1610743812)]
    int Install();
}

[ComImport]
[Guid("1444FDEF-9B72-4BEA-B1AC-36A77A3C5240")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateInstaller2 : IUpdateInstaller
{
    [DispId(1610743814)]
    bool RebootRequiredBeforeInstallation { get; }
}
