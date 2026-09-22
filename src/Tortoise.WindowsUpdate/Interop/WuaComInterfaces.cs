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
[Guid("704DD724-2EF1-4EDA-9FB7-A8AB4222F8C4")]
[CoClass(typeof(UpdateSessionClass))]
internal interface UpdateSession : IUpdateSession
{
}

[ComImport]
[Guid("704DD724-2EF1-4EDA-9FB7-A8AB4222F8C4")]
internal class UpdateSessionClass
{
}

[ComImport]
[Guid("AD785691-0349-4764-8753-6197C404DE8A")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateSession
{
    [return: MarshalAs(UnmanagedType.IDispatch)]
    object CreateUpdateSearcher();
}

[ComImport]
[Guid("652138BB-5572-412D-AD47-16CE87BE32F6")]
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
[Guid("816FD1E8-2DCC-40DE-881F-3F7381C8B558")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdateCollection
{
    [DispId(1610743810)]
    int Count { get; }

    [DispId(0)]
    IUpdate this[[In][MarshalAs(UnmanagedType.Struct)] int index] { get; }
}

[ComImport]
[Guid("7A601230-935E-4C3E-84BA-1E645CA106EC")]
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
[Guid("176DE312-0164-4CB3-ADAD-07D346EDF34D")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IUpdate2 : IUpdate
{
    [DispId(1610743830)]
    int AutoSelection { get; }
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
