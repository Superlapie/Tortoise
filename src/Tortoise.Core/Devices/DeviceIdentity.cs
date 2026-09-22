namespace Tortoise.Core.Devices;

public sealed record DeviceIdentity(
    string DeviceInstanceId,
    Guid? ContainerId,
    Guid ClassGuid,
    string ClassName,
    string FriendlyName,
    string Manufacturer,
    IReadOnlyList<string> HardwareIds,
    IReadOnlyList<string> CompatibleIds,
    string? BusType,
    string? Enumerator,
    string? Location,
    string? ParentInstanceId,
    bool IsPresent);
