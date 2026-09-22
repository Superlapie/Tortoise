using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Tortoise.Core.Devices;
using Tortoise.Core.Errors;
using Tortoise.Windows.Interop;

namespace Tortoise.Windows.Devices;

internal static class DevicePropertyReader
{
    internal static IReadOnlyList<string> EnumeratePresentDeviceInstanceIds()
    {
        var sizeResult = ConfigManagerNative.GetDeviceIdListSize(out var length, null, GetDeviceIdListFilter.Present);
        if (sizeResult != ConfigRet.Success || length == 0)
        {
            throw CreateInteropException("CM_Get_Device_ID_List_Size", sizeResult);
        }

        var buffer = new char[length];
        var result = ConfigManagerNative.GetDeviceIdList(null, buffer, length, GetDeviceIdListFilter.Present);
        if (result != ConfigRet.Success)
        {
            throw CreateInteropException("CM_Get_Device_ID_List", result);
        }

        return ParseMultiString(buffer);
    }

    internal static bool TryReadEntry(
        string deviceInstanceId,
        DeviceInfoSetScope infoSet,
        out DeviceInventoryEntry? entry,
        out string? warning)
    {
        entry = null;
        warning = null;

        var locateResult = ConfigManagerNative.LocateDevNode(out var devInst, deviceInstanceId, 0);
        if (locateResult is ConfigRet.NoSuchDevNode or ConfigRet.InvalidDevNode)
        {
            warning = $"Device disappeared during scan: {deviceInstanceId}";
            return false;
        }

        if (locateResult != ConfigRet.Success)
        {
            warning = $"Unable to locate device during scan ({deviceInstanceId}): {locateResult}";
            return false;
        }

        var statusResult = ConfigManagerNative.GetDevNodeStatus(
            out var status,
            out var problemNumber,
            devInst,
            0);

        if (statusResult != ConfigRet.Success)
        {
            warning = $"Unable to read status for {deviceInstanceId}: {statusResult}";
            return false;
        }

        var deviceInfoData = SpDevinfoData.Create();
        deviceInfoData.DevInst = devInst;

        if (!SetupApiNative.OpenDeviceInfo(infoSet.Handle, deviceInstanceId, IntPtr.Zero, 0, ref deviceInfoData))
        {
            warning = $"Device disappeared during scan: {deviceInstanceId}";
            return false;
        }

        var classGuid = ReadGuidProperty(infoSet.Handle, ref deviceInfoData, SetupDiRegistryProperty.ClassGuid)
                        ?? deviceInfoData.ClassGuid;
        if (classGuid == Guid.Empty)
        {
            classGuid = Guid.Empty;
        }

        var hardwareIds = ReadMultiStringRegistryProperty(
            infoSet.Handle,
            ref deviceInfoData,
            SetupDiRegistryProperty.HardwareId);
        var compatibleIds = ReadMultiStringRegistryProperty(
            infoSet.Handle,
            ref deviceInfoData,
            SetupDiRegistryProperty.CompatibleIds);

        var friendlyName = ReadStringRegistryProperty(infoSet.Handle, ref deviceInfoData, SetupDiRegistryProperty.FriendlyName)
                           ?? ReadStringRegistryProperty(infoSet.Handle, ref deviceInfoData, SetupDiRegistryProperty.DeviceDesc)
                           ?? deviceInstanceId;
        var manufacturer = ReadStringRegistryProperty(infoSet.Handle, ref deviceInfoData, SetupDiRegistryProperty.Mfg) ?? string.Empty;
        var className = ReadStringRegistryProperty(infoSet.Handle, ref deviceInfoData, SetupDiRegistryProperty.Class) ?? string.Empty;
        var location = ReadStringRegistryProperty(infoSet.Handle, ref deviceInfoData, SetupDiRegistryProperty.LocationInformation);
        var enumerator = ReadStringRegistryProperty(infoSet.Handle, ref deviceInfoData, SetupDiRegistryProperty.EnumeratorName);
        var busType = ReadStringRegistryProperty(infoSet.Handle, ref deviceInfoData, SetupDiRegistryProperty.BusTypeGuid);
        var parentInstanceId = ReadStringDeviceProperty(infoSet.Handle, ref deviceInfoData, DevPropKeys.DeviceParent);
        var containerId = ReadGuidDeviceProperty(infoSet.Handle, ref deviceInfoData, DevPropKeys.DeviceContainerId);

        var identity = new DeviceIdentity(
            deviceInstanceId,
            containerId,
            classGuid,
            className,
            friendlyName,
            manufacturer,
            hardwareIds,
            compatibleIds,
            busType,
            enumerator,
            location,
            parentInstanceId,
            IsPresent: true);

        var health = DeviceHealthMapper.Map(
            isPresent: true,
            configManagerStatus: status,
            problemCode: (int)problemNumber);

        var snapshot = new DeviceSnapshot(identity, health, DateTimeOffset.UtcNow);
        var installedDriver = ReadInstalledDriverBinding(infoSet.Handle, ref deviceInfoData);

        entry = new DeviceInventoryEntry(snapshot, installedDriver);
        return true;
    }

    private static DeviceDriverBinding? ReadInstalledDriverBinding(IntPtr infoSet, ref SpDevinfoData deviceInfoData)
    {
        var provider = ReadStringDeviceProperty(infoSet, ref deviceInfoData, DevPropKeys.DeviceDriverProvider);
        var versionText = ReadStringDeviceProperty(infoSet, ref deviceInfoData, DevPropKeys.DeviceDriverVersion);
        var infName = ReadStringDeviceProperty(infoSet, ref deviceInfoData, DevPropKeys.DeviceDriverInfPath);
        var driverDate = ReadDriverDate(infoSet, ref deviceInfoData);

        if (string.IsNullOrWhiteSpace(provider)
            && string.IsNullOrWhiteSpace(versionText)
            && driverDate is null
            && string.IsNullOrWhiteSpace(infName))
        {
            return null;
        }

        Version? version = null;
        if (!string.IsNullOrWhiteSpace(versionText) && Version.TryParse(versionText, out var parsedVersion))
        {
            version = parsedVersion;
        }

        return new DeviceDriverBinding(provider ?? string.Empty, version, driverDate, infName);
    }

    private static DateOnly? ReadDriverDate(IntPtr infoSet, ref SpDevinfoData deviceInfoData)
    {
        if (!SetupApiNative.GetDeviceProperty(
                infoSet,
                ref deviceInfoData,
                DevPropKeys.DeviceDriverDate,
                out var propertyType,
                [],
                0,
                out var requiredSize,
                SetupDiGetDevicePropertyFlags.Normal)
            && requiredSize == 0)
        {
            return null;
        }

        var buffer = new byte[requiredSize];
        if (!SetupApiNative.GetDeviceProperty(
                infoSet,
                ref deviceInfoData,
                DevPropKeys.DeviceDriverDate,
                out propertyType,
                buffer,
                requiredSize,
                out _,
                SetupDiGetDevicePropertyFlags.Normal))
        {
            return null;
        }

        if (propertyType != DevPropType.FileTime || buffer.Length < 8)
        {
            return null;
        }

        var fileTime = BitConverter.ToInt64(buffer, 0);
        if (fileTime <= 0)
        {
            return null;
        }

        try
        {
            var dateTime = DateTime.FromFileTimeUtc(fileTime);
            return DateOnly.FromDateTime(dateTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? ReadStringRegistryProperty(
        IntPtr infoSet,
        ref SpDevinfoData deviceInfoData,
        SetupDiRegistryProperty property)
    {
        if (!SetupApiNative.GetDeviceRegistryProperty(
                infoSet,
                ref deviceInfoData,
                property,
                out _,
                [],
                0,
                out var requiredSize)
            && requiredSize == 0)
        {
            return null;
        }

        var buffer = new byte[requiredSize];
        if (!SetupApiNative.GetDeviceRegistryProperty(
                infoSet,
                ref deviceInfoData,
                property,
                out _,
                buffer,
                requiredSize,
                out _))
        {
            return null;
        }

        return TrimNullTerminatedUnicode(buffer);
    }

    private static IReadOnlyList<string> ReadMultiStringRegistryProperty(
        IntPtr infoSet,
        ref SpDevinfoData deviceInfoData,
        SetupDiRegistryProperty property)
    {
        var value = ReadStringRegistryProperty(infoSet, ref deviceInfoData, property);
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        return value.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static Guid? ReadGuidProperty(
        IntPtr infoSet,
        ref SpDevinfoData deviceInfoData,
        SetupDiRegistryProperty property)
    {
        if (!SetupApiNative.GetDeviceRegistryProperty(
                infoSet,
                ref deviceInfoData,
                property,
                out _,
                [],
                0,
                out var requiredSize)
            && requiredSize == 0)
        {
            return null;
        }

        var buffer = new byte[requiredSize];
        if (!SetupApiNative.GetDeviceRegistryProperty(
                infoSet,
                ref deviceInfoData,
                property,
                out _,
                buffer,
                requiredSize,
                out _))
        {
            return null;
        }

        var text = TrimNullTerminatedUnicode(buffer);
        return Guid.TryParse(text, out var guid) ? guid : null;
    }

    private static string? ReadStringDeviceProperty(
        IntPtr infoSet,
        ref SpDevinfoData deviceInfoData,
        DevPropKey propertyKey)
    {
        if (!SetupApiNative.GetDeviceProperty(
                infoSet,
                ref deviceInfoData,
                propertyKey,
                out var propertyType,
                [],
                0,
                out var requiredSize,
                SetupDiGetDevicePropertyFlags.Normal)
            && requiredSize == 0)
        {
            return null;
        }

        var buffer = new byte[requiredSize];
        if (!SetupApiNative.GetDeviceProperty(
                infoSet,
                ref deviceInfoData,
                propertyKey,
                out propertyType,
                buffer,
                requiredSize,
                out _,
                SetupDiGetDevicePropertyFlags.Normal))
        {
            return null;
        }

        if (propertyType != DevPropType.String)
        {
            return null;
        }

        return TrimNullTerminatedUnicode(buffer);
    }

    private static Guid? ReadGuidDeviceProperty(
        IntPtr infoSet,
        ref SpDevinfoData deviceInfoData,
        DevPropKey propertyKey)
    {
        if (!SetupApiNative.GetDeviceProperty(
                infoSet,
                ref deviceInfoData,
                propertyKey,
                out var propertyType,
                [],
                0,
                out var requiredSize,
                SetupDiGetDevicePropertyFlags.Normal)
            && requiredSize == 0)
        {
            return null;
        }

        var buffer = new byte[requiredSize];
        if (!SetupApiNative.GetDeviceProperty(
                infoSet,
                ref deviceInfoData,
                propertyKey,
                out propertyType,
                buffer,
                requiredSize,
                out _,
                SetupDiGetDevicePropertyFlags.Normal))
        {
            return null;
        }

        if (propertyType != DevPropType.Guid || buffer.Length < 16)
        {
            return null;
        }

        return new Guid(buffer.AsSpan(0, 16));
    }

    private static IReadOnlyList<string> ParseMultiString(IReadOnlyList<char> buffer)
    {
        if (buffer.Count == 0)
        {
            return [];
        }

        var builder = new StringBuilder();
        var values = new List<string>();
        foreach (var character in buffer)
        {
            if (character == '\0')
            {
                if (builder.Length > 0)
                {
                    values.Add(builder.ToString());
                    builder.Clear();
                }

                continue;
            }

            builder.Append(character);
        }

        if (builder.Length > 0)
        {
            values.Add(builder.ToString());
        }

        return values;
    }

    private static string? TrimNullTerminatedUnicode(byte[] buffer)
    {
        if (buffer.Length == 0)
        {
            return null;
        }

        var length = buffer.Length;
        if (length >= 2 && buffer[^1] == 0 && buffer[^2] == 0)
        {
            length -= 2;
        }

        return Encoding.Unicode.GetString(buffer, 0, length).TrimEnd('\0');
    }

    private static TortoiseException CreateInteropException(string operation, ConfigRet result) =>
        new(
            TortoiseErrorCategory.InteropError,
            "Unable to enumerate devices on this PC.",
            $"{operation} failed with CONFIGRET 0x{((uint)result).ToString("X8", CultureInfo.InvariantCulture)}.");
}
