namespace Tortoise.Core.Devices;

public static class DeviceHealthMapper
{
    public const uint StatusDisabled = 0x00000020;
    public const uint StatusNeedRestart = 0x00000040;
    public const uint StatusHasProblem = 0x00000400;
    public const uint StatusDriverLoaded = 0x00000002;

    public static DeviceHealth Map(
        bool isPresent,
        uint configManagerStatus,
        int problemCode,
        bool disabledByPolicy = false)
    {
        if (!isPresent)
        {
            return new DeviceHealth(DeviceHealthState.NotPresent, null, "Device is not present.");
        }

        if (disabledByPolicy || (configManagerStatus & StatusDisabled) != 0)
        {
            return new DeviceHealth(DeviceHealthState.Disabled, null, "Device is disabled.");
        }

        if ((configManagerStatus & StatusNeedRestart) != 0)
        {
            return new DeviceHealth(
                DeviceHealthState.RestartRequired,
                problemCode == 0 ? null : problemCode,
                "Restart required for this device.");
        }

        if (problemCode != 0 || (configManagerStatus & StatusHasProblem) != 0)
        {
            return new DeviceHealth(
                DeviceHealthState.Problem,
                problemCode == 0 ? null : problemCode,
                DeviceProblemCodeCatalog.Describe(problemCode == 0 ? null : problemCode));
        }

        if ((configManagerStatus & StatusDriverLoaded) != 0)
        {
            return new DeviceHealth(DeviceHealthState.Healthy, null, "Device reports healthy status.");
        }

        return new DeviceHealth(DeviceHealthState.Unknown, null, "Driver status could not be determined.");
    }
}
