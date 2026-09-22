using System.Runtime.InteropServices;

namespace Tortoise.Windows.Interop;

internal static class NativeLibraries
{
    public const string CfgMgr32 = "cfgmgr32.dll";
    public const string SetupApi = "setupapi.dll";
}

internal enum ConfigRet : uint
{
    Success = 0,
    InvalidDevNode = 0x0000002D,
    NoSuchDevNode = 0x0000000D,
    BufferSmall = 0x0000001A,
}

[Flags]
internal enum GetDeviceIdListFilter : uint
{
    Present = 0x00000100,
}

[Flags]
internal enum DeviceNodeStatus : uint
{
    Disabled = 0x00000020,
    NeedRestart = 0x00000040,
    HasProblem = 0x00000400,
    DriverLoaded = 0x00000002,
}

internal enum SetupDiRegistryProperty : uint
{
    DeviceDesc = 0x00000000,
    HardwareId = 0x00000001,
    CompatibleIds = 0x00000002,
    Class = 0x00000007,
    ClassGuid = 0x00000008,
    Driver = 0x00000009,
    Mfg = 0x0000000B,
    FriendlyName = 0x0000000C,
    LocationInformation = 0x0000000D,
    BusTypeGuid = 0x00000015,
    EnumeratorName = 0x00000016,
}

internal enum SetupDiGetDevicePropertyFlags : uint
{
    Normal = 0x00000000,
}

internal enum DevPropType : uint
{
    String = 0x00000012,
    Guid = 0x0000000D,
    FileTime = 0x00000010,
}

[StructLayout(LayoutKind.Sequential)]
internal struct DevPropKey
{
    public Guid FormatId;
    public uint PropertyId;
}

internal static class DevPropKeys
{
    // DEVPKEY_Device_* driver properties (Microsoft-defined property set)
    private static readonly Guid DriverPropertySet =
        new("A8B865DD-2E3D-4094-AD97-E593A70C75D6");

    public static readonly DevPropKey DeviceParent = new()
    {
        FormatId = new Guid("4340A6C5-93FA-4706-972F-7A732D896B00"),
        PropertyId = 8,
    };

    public static readonly DevPropKey DeviceContainerId = new()
    {
        FormatId = new Guid("8C7ED41F-1508-11D1-AD59-00C04FD9596D"),
        PropertyId = 2,
    };

    public static readonly DevPropKey DeviceDriverProvider = new()
    {
        FormatId = DriverPropertySet,
        PropertyId = 13,
    };

    public static readonly DevPropKey DeviceDriverVersion = new()
    {
        FormatId = DriverPropertySet,
        PropertyId = 14,
    };

    public static readonly DevPropKey DeviceDriverDate = new()
    {
        FormatId = DriverPropertySet,
        PropertyId = 11,
    };

    public static readonly DevPropKey DeviceDriverInfPath = new()
    {
        FormatId = DriverPropertySet,
        PropertyId = 12,
    };

    public static readonly DevPropKey DeviceClassGuid = new()
    {
        FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        PropertyId = 14,
    };
}

internal enum ConfigRegistryProperty : uint
{
    ClassGuid = 0x00000008,
}

internal static partial class ConfigManagerNative
{
    [LibraryImport(NativeLibraries.CfgMgr32, EntryPoint = "CM_Get_Device_ID_List_SizeW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial ConfigRet GetDeviceIdListSize(
        out uint length,
        string? filterInstanceId,
        GetDeviceIdListFilter filter);

    [LibraryImport(NativeLibraries.CfgMgr32, EntryPoint = "CM_Get_Device_ID_ListW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial ConfigRet GetDeviceIdList(
        string? filterInstanceId,
        char[] buffer,
        uint bufferLength,
        GetDeviceIdListFilter filter);

    [LibraryImport(NativeLibraries.CfgMgr32, EntryPoint = "CM_Locate_DevNodeW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial ConfigRet LocateDevNode(
        out uint deviceInstanceHandle,
        string deviceInstanceId,
        uint flags);

    [LibraryImport(NativeLibraries.CfgMgr32, EntryPoint = "CM_Get_DevNode_Status")]
    internal static partial ConfigRet GetDevNodeStatus(
        out uint status,
        out uint problemNumber,
        uint deviceInstanceHandle,
        uint flags);

    [LibraryImport(NativeLibraries.CfgMgr32, EntryPoint = "CM_Get_DevNode_Registry_PropertyW")]
    internal static partial ConfigRet GetDevNodeRegistryProperty(
        uint deviceInstanceHandle,
        ConfigRegistryProperty property,
        out uint propertyDataType,
        IntPtr buffer,
        ref uint bufferLength,
        uint flags);

    [LibraryImport(NativeLibraries.CfgMgr32, EntryPoint = "CM_Get_DevNode_PropertyW")]
    internal static partial ConfigRet GetDevNodeProperty(
        uint deviceInstanceHandle,
        in DevPropKey propertyKey,
        out DevPropType propertyType,
        IntPtr buffer,
        ref uint bufferLength,
        uint flags);

    [LibraryImport(NativeLibraries.CfgMgr32, EntryPoint = "CM_Get_Parent")]
    internal static partial ConfigRet GetParent(
        out uint parentDeviceInstanceHandle,
        uint deviceInstanceHandle,
        uint flags);
}

internal static partial class SetupApiNative
{
    [LibraryImport(NativeLibraries.SetupApi, EntryPoint = "SetupDiCreateDeviceInfoList")]
    internal static partial IntPtr CreateDeviceInfoList(IntPtr classGuid, IntPtr parentWindow);

    [LibraryImport(NativeLibraries.SetupApi, EntryPoint = "SetupDiDestroyDeviceInfoList")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyDeviceInfoList(IntPtr deviceInfoSet);

    [LibraryImport(NativeLibraries.SetupApi, EntryPoint = "SetupDiOpenDeviceInfoW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenDeviceInfo(
        IntPtr deviceInfoSet,
        string deviceInstanceId,
        IntPtr parentWindow,
        uint openFlags,
        ref SpDevinfoData deviceInfoData);

    [LibraryImport(NativeLibraries.SetupApi, EntryPoint = "SetupDiGetDeviceRegistryPropertyW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        SetupDiRegistryProperty property,
        out uint propertyRegDataType,
        byte[] propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [LibraryImport(NativeLibraries.SetupApi, EntryPoint = "SetupDiGetDevicePropertyW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetDeviceProperty(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        in DevPropKey propertyKey,
        out DevPropType propertyType,
        byte[] propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize,
        SetupDiGetDevicePropertyFlags flags);
}

[StructLayout(LayoutKind.Sequential)]
internal struct SpDevinfoData
{
    public uint Size;
    public Guid ClassGuid;
    public uint DevInst;
    public IntPtr Reserved;

    public static SpDevinfoData Create() =>
        new()
        {
            Size = (uint)Marshal.SizeOf<SpDevinfoData>(),
        };
}
