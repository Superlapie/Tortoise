using Tortoise.VmHarness.Domain;
using Tortoise.VmHarness.Guest;
using Tortoise.VmHarness.Storage;

namespace Tortoise.VmHarness.Scenarios;

public sealed class VmHarnessScenarioContext
{
    public required VmHarnessRun Run { get; init; }

    public required VmHarnessRunOptions Options { get; init; }

    public required VmHarnessVmTarget Target { get; init; }

    public VmHarnessCheckpoint? Checkpoint { get; init; }

    public VmHarnessGuestEnvironmentProof? GuestProof { get; init; }

    public IVmHarnessGuestTransport? GuestTransport { get; init; }

    public IVmHarnessRunStore RunStore { get; init; } = null!;

    public bool ExecuteMutation { get; init; }

    public CancellationToken CancellationToken { get; init; }
}

public interface IVmHarnessScenario
{
    VmHarnessScenarioDefinition Definition { get; }

    Task<VmHarnessScenarioResult> ExecuteAsync(
        VmHarnessScenarioContext context,
        CancellationToken cancellationToken = default);
}
