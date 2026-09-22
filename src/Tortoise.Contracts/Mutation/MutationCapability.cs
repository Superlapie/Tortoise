namespace Tortoise.Contracts.Mutation;

/// <summary>
/// Default production-safe mutation gate. Mutation is disabled unless explicitly enabled
/// through controlled configuration that satisfies progressive capability gates.
/// </summary>
public sealed class MutationCapability : IMutationCapability
{
    public static MutationCapability ReadOnly { get; } = new(
        isEnabled: false,
        environment: MutationEnvironment.ReadOnly,
        reason: "Mutation is globally disabled during early development.");

    /// <summary>
    /// Simulated workflow preset. Mutation remains disabled; the environment marks
    /// end-to-end simulation paths that never touch real driver APIs.
    /// </summary>
    public static MutationCapability Simulated { get; } = new(
        isEnabled: false,
        environment: MutationEnvironment.Simulated,
        reason: "Simulated workflow only; no driver mutation is performed.");

    public MutationCapability(bool isEnabled, MutationEnvironment environment, string reason)
    {
        IsEnabled = isEnabled;
        Environment = environment;
        Reason = reason;
    }

    public bool IsEnabled { get; }

    public MutationEnvironment Environment { get; }

    public string Reason { get; }
}
