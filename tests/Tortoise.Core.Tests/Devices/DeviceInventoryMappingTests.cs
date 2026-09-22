using Tortoise.Core.Devices;

namespace Tortoise.Core.Tests.Devices;

public sealed class DeviceHealthMapperTests
{
    [Fact]
    public void Map_marks_problem_code_10_as_problem()
    {
        var health = DeviceHealthMapper.Map(
            isPresent: true,
            configManagerStatus: DeviceHealthMapper.StatusHasProblem,
            problemCode: 10);

        Assert.Equal(DeviceHealthState.Problem, health.State);
        Assert.Equal(10, health.ProblemCode);
        Assert.Contains("cannot start", health.ProblemDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Map_marks_disabled_status_as_disabled()
    {
        var health = DeviceHealthMapper.Map(
            isPresent: true,
            configManagerStatus: DeviceHealthMapper.StatusDisabled,
            problemCode: 0);

        Assert.Equal(DeviceHealthState.Disabled, health.State);
    }

    [Fact]
    public void Map_marks_restart_required_status()
    {
        var health = DeviceHealthMapper.Map(
            isPresent: true,
            configManagerStatus: DeviceHealthMapper.StatusNeedRestart,
            problemCode: 0);

        Assert.Equal(DeviceHealthState.RestartRequired, health.State);
    }

    [Fact]
    public void Map_marks_loaded_driver_without_problem_as_healthy()
    {
        var health = DeviceHealthMapper.Map(
            isPresent: true,
            configManagerStatus: DeviceHealthMapper.StatusDriverLoaded,
            problemCode: 0);

        Assert.Equal(DeviceHealthState.Healthy, health.State);
    }
}

public sealed class DeviceProblemCodeCatalogTests
{
    [Fact]
    public void Describe_returns_known_text_for_code_10()
    {
        var description = DeviceProblemCodeCatalog.Describe(10);

        Assert.Contains("cannot start", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_returns_fallback_for_unknown_code()
    {
        var description = DeviceProblemCodeCatalog.Describe(9999);

        Assert.Contains("9999", description, StringComparison.Ordinal);
    }
}
