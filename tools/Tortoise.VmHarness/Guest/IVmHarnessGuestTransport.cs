using Tortoise.VmHarness.Domain;

namespace Tortoise.VmHarness.Guest;

public sealed record VmHarnessGuestCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration);

public interface IVmHarnessGuestTransport
{
    bool IsConfigured { get; }

    Task<VmHarnessGuestEnvironmentProof> GetEnvironmentProofAsync(
        string vmName,
        CancellationToken cancellationToken = default);

    Task<VmHarnessGuestCommandResult> ExecuteCommandAsync(
        string vmName,
        string command,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default);
}
