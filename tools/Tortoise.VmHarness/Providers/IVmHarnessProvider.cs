using Tortoise.VmHarness.Domain;

namespace Tortoise.VmHarness.Providers;

public sealed class VmHarnessProviderException : Exception
{
    public VmHarnessProviderException(string message) : base(message)
    {
    }

    public VmHarnessProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public interface IVmHarnessProvider
{
    string ProviderName { get; }

    Task EnsureHostRequirementsAsync(CancellationToken cancellationToken = default);

    Task<VmHarnessVmTarget> ResolveVmAsync(string vmName, CancellationToken cancellationToken = default);

    Task<VmHarnessCheckpoint> CreateCheckpointAsync(
        VmHarnessVmTarget target,
        string checkpointName,
        CancellationToken cancellationToken = default);

    Task<VmHarnessRestoreResult> RestoreCheckpointAsync(
        VmHarnessVmTarget target,
        VmHarnessCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    Task WaitForVmRunningAsync(
        VmHarnessVmTarget target,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
