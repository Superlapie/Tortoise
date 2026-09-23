using Tortoise.VmHarness.Domain;

namespace Tortoise.VmHarness.Storage;

public interface IVmHarnessRunStore
{
    Task<VmHarnessRun> CreateRunAsync(VmHarnessRunOptions options, CancellationToken cancellationToken = default);

    string GetRunDirectory(VmHarnessRun run);

    Task WriteJsonArtifactAsync<T>(
        VmHarnessRun run,
        string fileName,
        T payload,
        CancellationToken cancellationToken = default);

    Task WriteTextArtifactAsync(
        VmHarnessRun run,
        string fileName,
        string content,
        CancellationToken cancellationToken = default);

    Task WriteSanitizedLogAsync(
        VmHarnessRun run,
        string fileName,
        string rawLog,
        CancellationToken cancellationToken = default);

    Task<VmHarnessEvidenceManifest> FinalizeManifestAsync(
        VmHarnessRun run,
        VmHarnessEvidenceManifest manifest,
        CancellationToken cancellationToken = default);
}
