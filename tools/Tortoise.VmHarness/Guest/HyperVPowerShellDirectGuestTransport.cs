using System.Runtime.Versioning;
using System.Text.Json;
using Tortoise.VmHarness.Domain;
using Tortoise.VmHarness.Providers.HyperV;

namespace Tortoise.VmHarness.Guest;

[SupportedOSPlatform("windows")]
public sealed class HyperVPowerShellDirectGuestTransport : IVmHarnessGuestTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HyperVPowerShellExecutor _executor = new();

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TORTOISE_VM_HARNESS_GUEST_USER"))
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TORTOISE_VM_HARNESS_GUEST_PASSWORD"));

    public async Task<VmHarnessGuestEnvironmentProof> GetEnvironmentProofAsync(
        VmHarnessGuestTarget target,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteRemoteAsync(
            target,
            "tortoise-lab status --json",
            VmHarnessLabEnvironment.MutationCommandEnvironment,
            cancellationToken);
        var status = HarnessLabJsonParser.ParseStatusJson(result.StandardOutput.Trim());
        return VmHarnessGuestStatusParser.FromLabStatusJson(status, result.StandardOutput.Trim());
    }

    public async Task<VmHarnessGuestCommandResult> ExecuteCommandAsync(
        VmHarnessGuestTarget target,
        string command,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        var remote = await ExecuteRemoteAsync(target, command, environmentVariables, cancellationToken);
        return VmHarnessGuestCommandResult.FromRemote(remote);
    }

    public async Task<bool> ProbeReachabilityAsync(
        VmHarnessGuestTarget target,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return false;
        }

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await ExecuteRemoteAsync(
                    target,
                    "cmd /c echo tortoise-harness-probe",
                    null,
                    cancellationToken);
                if (result.ExitCode == 0
                    && result.StandardOutput.Contains("tortoise-harness-probe", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch
            {
                // Keep polling until timeout.
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        return false;
    }

    private async Task<VmHarnessRemoteCommandResult> ExecuteRemoteAsync(
        VmHarnessGuestTarget target,
        string command,
        IReadOnlyDictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Guest transport is not configured. Set TORTOISE_VM_HARNESS_GUEST_USER and TORTOISE_VM_HARNESS_GUEST_PASSWORD.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(target.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.HyperVVmId);

        var escapedVmId = EscapeSingleQuoted(target.HyperVVmId);
        var escapedVmName = EscapeSingleQuoted(target.Name);
        var escapedCommand = EscapePowerShellSingleQuoted(command);
        var envScript = BuildGuestEnvironmentScript(environmentVariables);
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            if ([string]::IsNullOrWhiteSpace($env:TORTOISE_VM_HARNESS_GUEST_USER) -or [string]::IsNullOrWhiteSpace($env:TORTOISE_VM_HARNESS_GUEST_PASSWORD)) {
              throw 'Guest credentials are not available to the host PowerShell process environment.'
            }
            Import-Module Hyper-V -ErrorAction Stop | Out-Null
            $vm = Get-VM -Id '{{escapedVmId}}' -ErrorAction Stop
            if ($vm.Name -cne '{{escapedVmName}}') {
              throw "Hyper-V VM ID/name binding mismatch for guest transport (expected '{{escapedVmName}}', got '$($vm.Name)')."
            }
            $sec = ConvertTo-SecureString $env:TORTOISE_VM_HARNESS_GUEST_PASSWORD -AsPlainText -Force
            $cred = New-Object System.Management.Automation.PSCredential($env:TORTOISE_VM_HARNESS_GUEST_USER, $sec)
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $remote = Invoke-Command -VMName $vm.Name -Credential $cred -ScriptBlock {
              param([string]$CommandLine, [string]$EnvSetup)
              if (-not [string]::IsNullOrWhiteSpace($EnvSetup)) {
                Invoke-Expression $EnvSetup
              }
              $stdout = cmd /c $CommandLine 2>&1 | Out-String
              $stderr = ''
              [pscustomobject]@{
                ExitCode = $LASTEXITCODE
                StandardOutput = $stdout
                StandardError = $stderr
              }
            } -ArgumentList '{{escapedCommand}}', '{{EscapePowerShellSingleQuoted(envScript)}}'
            $sw.Stop()
            [pscustomobject]@{
              ExitCode = [int]$remote.ExitCode
              StandardOutput = [string]$remote.StandardOutput
              StandardError = [string]$remote.StandardError
              DurationMs = [int]$sw.ElapsedMilliseconds
            } | ConvertTo-Json -Compress
            """;

        var hostEnvironment = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TORTOISE_VM_HARNESS_GUEST_USER")))
        {
            hostEnvironment["TORTOISE_VM_HARNESS_GUEST_USER"] =
                Environment.GetEnvironmentVariable("TORTOISE_VM_HARNESS_GUEST_USER")!;
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TORTOISE_VM_HARNESS_GUEST_PASSWORD")))
        {
            hostEnvironment["TORTOISE_VM_HARNESS_GUEST_PASSWORD"] =
                Environment.GetEnvironmentVariable("TORTOISE_VM_HARNESS_GUEST_PASSWORD")!;
        }

        var json = await _executor.RunAsync(script, hostEnvironment, cancellationToken);
        var remote = JsonSerializer.Deserialize<VmHarnessRemoteCommandResult>(json, JsonOptions)
            ?? throw new InvalidOperationException("Guest command returned invalid JSON.");
        return remote;
    }

    private static string BuildGuestEnvironmentScript(IReadOnlyDictionary<string, string>? environmentVariables)
    {
        if (environmentVariables is null || environmentVariables.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "; ",
            environmentVariables.Select(pair =>
                $"Set-Item -Path Env:{pair.Key} -Value '{EscapeSingleQuoted(pair.Value)}'"));
    }

    private static string EscapeSingleQuoted(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private static string EscapePowerShellSingleQuoted(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}

public sealed class UnconfiguredGuestTransport : IVmHarnessGuestTransport
{
    public bool IsConfigured => false;

    public Task<VmHarnessGuestEnvironmentProof> GetEnvironmentProofAsync(
        VmHarnessGuestTarget target,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Guest transport is not configured.");

    public Task<VmHarnessGuestCommandResult> ExecuteCommandAsync(
        VmHarnessGuestTarget target,
        string command,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Guest transport is not configured.");

    public Task<bool> ProbeReachabilityAsync(
        VmHarnessGuestTarget target,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
