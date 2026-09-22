namespace Tortoise.Core.Devices;

public enum DeviceHealthState
{
    Healthy = 0,
    Problem = 1,
    Disabled = 2,
    NotPresent = 3,
    RestartRequired = 4,
    Unknown = 5,
}

public sealed record DeviceHealth(
    DeviceHealthState State,
    int? ProblemCode,
    string? ProblemDescription);

public sealed record DeviceSnapshot(
    DeviceIdentity Identity,
    DeviceHealth Health,
    DateTimeOffset CapturedAtUtc);
