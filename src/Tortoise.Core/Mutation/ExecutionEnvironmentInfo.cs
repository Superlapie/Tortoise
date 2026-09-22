namespace Tortoise.Core.Mutation;

public sealed record ExecutionEnvironmentInfo(
    bool IsDisposableVm,
    bool MutationTestsEnabled,
    bool VmInstallExplicitlyAllowed,
    bool PhysicalPilotExplicitlyAllowed = false);

public interface IExecutionEnvironmentDetector
{
    ExecutionEnvironmentInfo Detect();
}

public sealed class StaticExecutionEnvironmentDetector : IExecutionEnvironmentDetector
{
    private readonly ExecutionEnvironmentInfo _environment;

    public StaticExecutionEnvironmentDetector(ExecutionEnvironmentInfo environment) =>
        _environment = environment;

    public ExecutionEnvironmentInfo Detect() => _environment;
}

public sealed class UnsupportedExecutionEnvironmentDetector : IExecutionEnvironmentDetector
{
    public ExecutionEnvironmentInfo Detect() =>
        new(
            IsDisposableVm: false,
            MutationTestsEnabled: false,
            VmInstallExplicitlyAllowed: false,
            PhysicalPilotExplicitlyAllowed: false);
}
