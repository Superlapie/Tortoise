namespace Tortoise.Contracts.Mutation;

/// <summary>
/// Hard gate for any operation that can change system or device state.
/// During early development, <see cref="IsEnabled"/> must remain false.
/// </summary>
public interface IMutationCapability
{
    bool IsEnabled { get; }

    MutationEnvironment Environment { get; }

    string Reason { get; }
}

public enum MutationEnvironment
{
    ReadOnly = 0,
    Simulated = 1,
    DisposableVm = 2,
    PhysicalPilot = 3,
}
