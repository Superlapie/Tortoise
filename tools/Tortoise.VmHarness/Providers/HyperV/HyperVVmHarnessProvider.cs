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

        _ = await _executor.RunAsync(script, cancellationToken);
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

        var record = await _executor.RunJsonAsync<HyperVVmRecord>(script, cancellationToken)
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

    public async Task<VmHarnessCheckpoint> CreateCheckpointAsync(
        VmHarnessVmTarget target,
        string checkpointName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointName);

        var escapedVm = EscapeSingleQuoted(target.Name);
        var escapedCheckpoint = EscapeSingleQuoted(checkpointName);
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            Import-Module Hyper-V -ErrorAction Stop | Out-Null
            $vm = Get-VM -Name '{{escapedVm}}' -ErrorAction Stop
            if ($vm.State -ne 'Running') {
              Start-VM -VM $vm | Out-Null
            }
            Checkpoint-VM -VM $vm -SnapshotName '{{escapedCheckpoint}}' -ErrorAction Stop | Out-Null
            $snapshot = Get-VMSnapshot -VMName '{{escapedVm}}' -Name '{{escapedCheckpoint}}' -ErrorAction Stop
            [pscustomobject]@{
              Id = $snapshot.Id.Guid.ToString()
              Name = $snapshot.Name
              CreationTime = $snapshot.CreationTime.ToUniversalTime().ToString('o')
            } | ConvertTo-Json -Compress
            """;

        var snapshot = await _executor.RunJsonAsync<HyperVSnapshotRecord>(script, cancellationToken)
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

        var escapedVm = EscapeSingleQuoted(target.Name);
        var escapedCheckpoint = EscapeSingleQuoted(checkpoint.Name);
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            Import-Module Hyper-V -ErrorAction Stop | Out-Null
            Restore-VMSnapshot -VMName '{{escapedVm}}' -Name '{{escapedCheckpoint}}' -Confirm:$false -ErrorAction Stop | Out-Null
            Start-VM -Name '{{escapedVm}}' -ErrorAction Stop | Out-Null
            $vm = Get-VM -Name '{{escapedVm}}' -ErrorAction Stop
            [pscustomobject]@{
              State = $vm.State.ToString()
            } | ConvertTo-Json -Compress
            """;

        try
        {
            var state = await _executor.RunJsonAsync<HyperVVmRecord>(script, cancellationToken);
            var reachable = string.Equals(state?.State, "Running", StringComparison.OrdinalIgnoreCase);
            return new VmHarnessRestoreResult(
                reachable,
                checkpoint.Name,
                checkpoint.Id,
                reachable,
                reachable
                    ? $"Restored checkpoint '{checkpoint.Name}' and VM is running."
                    : $"Restored checkpoint '{checkpoint.Name}' but VM state is '{state?.State ?? "unknown"}'.");
        }
        catch (Exception ex)
        {
            return new VmHarnessRestoreResult(
                false,
                checkpoint.Name,
                checkpoint.Id,
                false,
                "Checkpoint restore failed.",
                ex.Message);
        }
    }

    public async Task WaitForVmRunningAsync(
        VmHarnessVmTarget target,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await ResolveVmAsync(target.Name, cancellationToken);
            if (string.Equals(current.State, "Running", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new VmHarnessProviderException(
            $"Timed out waiting for VM '{target.Name}' to reach Running state.");
    }

    private static string EscapeSingleQuoted(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}
