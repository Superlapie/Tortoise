using System.Runtime.Versioning;
using Tortoise.VmHarness.Domain;

namespace Tortoise.VmHarness.Providers.HyperV;

[SupportedOSPlatform("windows")]
public sealed class HyperVVmHarnessProvider : IVmHarnessProvider
{
    private readonly HyperVPowerShellExecutor _executor = new();

    public string ProviderName => "Hyper-V";

    public async Task EnsureHostRequirementsAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new VmHarnessProviderException(
                "Hyper-V harness provider requires a Windows host.");
        }

        const string script = """
            $ErrorActionPreference = 'Stop'
            if (-not (Get-Module -ListAvailable -Name Hyper-V)) {
              throw 'Hyper-V PowerShell module is not available on this host.'
            }
            Import-Module Hyper-V -ErrorAction Stop | Out-Null
            'ok'
            """;

        _ = await _executor.RunAsync(script, cancellationToken: cancellationToken);
    }

    public async Task<VmHarnessVmTarget> ResolveVmAsync(string vmName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vmName);

        var escapedName = EscapeSingleQuoted(vmName);
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            Import-Module Hyper-V -ErrorAction Stop | Out-Null
            $matches = @(Get-VM -Name '*' | Where-Object { $_.Name -ceq '{{escapedName}}' })
            if ($matches.Count -eq 0) {
              throw "No Hyper-V VM matched the exact name '{{escapedName}}'."
            }
            if ($matches.Count -gt 1) {
              throw "Ambiguous Hyper-V VM name '{{escapedName}}' matched multiple VMs."
            }
            $vm = $matches[0]
            [pscustomobject]@{
              Id = $vm.Id.Guid.ToString()
              Name = $vm.Name
              State = $vm.State.ToString()
            } | ConvertTo-Json -Compress
            """;

        var record = await _executor.RunJsonAsync<HyperVVmRecord>(script, cancellationToken: cancellationToken)
            ?? throw new VmHarnessProviderException($"Hyper-V VM '{vmName}' could not be resolved.");

        if (!string.Equals(record.Name, vmName, StringComparison.Ordinal))
        {
            throw new VmHarnessProviderException(
                $"Hyper-V VM name mismatch after resolution (requested '{vmName}', got '{record.Name}').");
        }

        return new VmHarnessVmTarget(
            record.Name,
            record.Id,
            record.State,
            CheckpointOperationsAvailable: true);
    }

    public async Task<VmHarnessVmTarget> VerifyTargetIdentityAsync(
        VmHarnessVmTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var record = await GetVmByIdAsync(target.HyperVVmId, cancellationToken);
        if (!string.Equals(record.Name, target.Name, StringComparison.Ordinal))
        {
            throw new VmHarnessProviderException(
                $"Hyper-V VM ID '{target.HyperVVmId}' now resolves to '{record.Name}', expected '{target.Name}'.");
        }

        return target with { State = record.State };
    }

    public async Task<VmHarnessCheckpoint> CreateCheckpointAsync(
        VmHarnessVmTarget target,
        string checkpointName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointName);

        target = await VerifyTargetIdentityAsync(target, cancellationToken);
        var escapedCheckpoint = EscapeSingleQuoted(checkpointName);
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            Import-Module Hyper-V -ErrorAction Stop | Out-Null
            $vm = Get-VM -Id '{{target.HyperVVmId}}' -ErrorAction Stop
            if ($vm.Name -cne '{{EscapeSingleQuoted(target.Name)}}') {
              throw 'Hyper-V VM ID/name binding mismatch during checkpoint creation.'
            }
            if ($vm.State -ne 'Running') {
              Start-VM -VM $vm | Out-Null
            }
            Checkpoint-VM -VM $vm -SnapshotName '{{escapedCheckpoint}}' -ErrorAction Stop | Out-Null
            $snapshot = Get-VMSnapshot -VM $vm -Name '{{escapedCheckpoint}}' -ErrorAction Stop
            [pscustomobject]@{
              Id = $snapshot.Id.Guid.ToString()
              Name = $snapshot.Name
              CreationTime = $snapshot.CreationTime.ToUniversalTime().ToString('o')
            } | ConvertTo-Json -Compress
            """;

        var snapshot = await _executor.RunJsonAsync<HyperVSnapshotRecord>(script, cancellationToken: cancellationToken)
            ?? throw new VmHarnessProviderException(
                $"Failed to create checkpoint '{checkpointName}' for VM '{target.Name}'.");

        return new VmHarnessCheckpoint(
            snapshot.Name,
            snapshot.Id,
            DateTime.Parse(snapshot.CreationTime).ToUniversalTime(),
            target.HyperVVmId,
            target.Name);
    }

    public async Task<VmHarnessRestoreResult> RestoreCheckpointAsync(
        VmHarnessVmTarget target,
        VmHarnessCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (!string.Equals(checkpoint.HyperVVmId, target.HyperVVmId, StringComparison.OrdinalIgnoreCase))
        {
            return new VmHarnessRestoreResult(
                false,
                checkpoint.Name,
                checkpoint.Id,
                false,
                false,
                "Checkpoint VM ID does not match the bound target VM ID.",
                "Checkpoint/target VM ID mismatch.");
        }

        try
        {
            target = await VerifyTargetIdentityAsync(target, cancellationToken);
            var escapedCheckpoint = EscapeSingleQuoted(checkpoint.Name);
            var script = $$"""
                $ErrorActionPreference = 'Stop'
                Import-Module Hyper-V -ErrorAction Stop | Out-Null
                $vm = Get-VM -Id '{{target.HyperVVmId}}' -ErrorAction Stop
                if ($vm.Name -cne '{{EscapeSingleQuoted(target.Name)}}') {
                  throw 'Hyper-V VM ID/name binding mismatch during restore.'
                }
                Restore-VMSnapshot -VM $vm -Name '{{escapedCheckpoint}}' -Confirm:$false -ErrorAction Stop | Out-Null
                Start-VM -VM $vm | Out-Null
                $vm = Get-VM -Id '{{target.HyperVVmId}}' -ErrorAction Stop
                [pscustomobject]@{
                  State = $vm.State.ToString()
                } | ConvertTo-Json -Compress
                """;

            var state = await _executor.RunJsonAsync<HyperVStateRecord>(script, cancellationToken: cancellationToken);
            var hyperVRunning = string.Equals(state?.State, "Running", StringComparison.OrdinalIgnoreCase);
            return new VmHarnessRestoreResult(
                hyperVRunning,
                checkpoint.Name,
                checkpoint.Id,
                hyperVRunning,
                false,
                hyperVRunning
                    ? $"Restored checkpoint '{checkpoint.Name}' and Hyper-V reports Running."
                    : $"Restored checkpoint '{checkpoint.Name}' but Hyper-V state is '{state?.State ?? "unknown"}'.");
        }
        catch (Exception ex)
        {
            return new VmHarnessRestoreResult(
                false,
                checkpoint.Name,
                checkpoint.Id,
                false,
                false,
                "Checkpoint restore failed.",
                ex.Message);
        }
    }

    public async Task<bool> WaitForHyperVRunningAsync(
        VmHarnessVmTarget target,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await VerifyTargetIdentityAsync(target, cancellationToken);
            if (string.Equals(current.State, "Running", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        return false;
    }

    private async Task<HyperVVmRecord> GetVmByIdAsync(string vmId, CancellationToken cancellationToken)
    {
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            Import-Module Hyper-V -ErrorAction Stop | Out-Null
            $vm = Get-VM -Id '{{vmId}}' -ErrorAction Stop
            [pscustomobject]@{
              Id = $vm.Id.Guid.ToString()
              Name = $vm.Name
              State = $vm.State.ToString()
            } | ConvertTo-Json -Compress
            """;

        return await _executor.RunJsonAsync<HyperVVmRecord>(script, cancellationToken: cancellationToken)
            ?? throw new VmHarnessProviderException($"Hyper-V VM ID '{vmId}' could not be resolved.");
    }

    private static string EscapeSingleQuoted(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}
