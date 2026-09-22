namespace Tortoise.Core.Devices;

public sealed record DeviceDriverBinding(
    string ProviderName,
    Version? DriverVersion,
    DateOnly? DriverDate,
    string? InfName);

public sealed record DeviceInventoryEntry(
    DeviceSnapshot Snapshot,
    DeviceDriverBinding? InstalledDriver);

public sealed record DeviceInventoryScanResult(
    IReadOnlyList<DeviceInventoryEntry> Devices,
    IReadOnlyList<string> Warnings,
    DateTimeOffset ScannedAtUtc);
