using System.Text.Json;
using System.Text.Json.Serialization;
using Tortoise.VmHarness.Domain;
using Tortoise.VmHarness.Evidence;

namespace Tortoise.VmHarness.Storage;

public sealed class FileSystemVmHarnessRunStore : IVmHarnessRunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _artifactsRoot;

    public FileSystemVmHarnessRunStore(string artifactsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactsRoot);
        _artifactsRoot = artifactsRoot;
    }

    public Task<VmHarnessRun> CreateRunAsync(
        VmHarnessRunOptions options,
        CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N");
        var run = new VmHarnessRun(
            runId,
            options.VmName,
            options.ScenarioId,
            options.Mode,
            DateTimeOffset.UtcNow,
            options.TortoiseCommitSha,
            VmHarnessConstants.HarnessVersion);

        var directory = GetRunDirectory(run);
        Directory.CreateDirectory(directory);
        return Task.FromResult(run);
    }

    public string GetRunDirectory(VmHarnessRun run) =>
        Path.Combine(_artifactsRoot, run.RunId);

    public async Task WriteJsonArtifactAsync<T>(
        VmHarnessRun run,
        string fileName,
        T payload,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(GetRunDirectory(run), fileName);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public async Task WriteTextArtifactAsync(
        VmHarnessRun run,
        string fileName,
        string content,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(GetRunDirectory(run), fileName);
        await File.WriteAllTextAsync(path, VmHarnessRedactor.Redact(content), cancellationToken);
    }

    public async Task WriteSanitizedLogAsync(
        VmHarnessRun run,
        string fileName,
        string rawLog,
        CancellationToken cancellationToken = default)
    {
        await WriteTextArtifactAsync(run, fileName, VmHarnessRedactor.Redact(rawLog), cancellationToken);
    }

    public async Task<VmHarnessEvidenceManifest> FinalizeManifestAsync(
        VmHarnessRun run,
        VmHarnessEvidenceManifest manifest,
        CancellationToken cancellationToken = default)
    {
        await WriteJsonArtifactAsync(run, "verification.json", manifest, cancellationToken);
        return manifest;
    }
}
