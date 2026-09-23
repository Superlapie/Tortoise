using System.Diagnostics;
using System.Runtime.Versioning;
using Tortoise.VmHarness.Domain;
using Tortoise.VmHarness.Providers.HyperV;

namespace Tortoise.VmHarness.Guest;

[SupportedOSPlatform("windows")]
public sealed class HyperVPowerShellDirectGuestTransport : IVmHarnessGuestTransport
{
    private readonly HyperVPowerShellExecutor _executor = new();

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TORTOISE_VM_HARNESS_GUEST_USER"));

    public async Task<VmHarnessGuestEnvironmentProof> GetEnvironmentProofAsync(
        string vmName,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteCommandAsync(
            vmName,
            "tortoise-lab status",
            new Dictionary<string, string>
            {
                ["TORTOISE_MUTATION_TESTS"] = "1",
                ["TORTOISE_ALLOW_VM_INSTALL"] = "1",
            },
            cancellationToken);

        return VmHarnessGuestStatusParser.ParseStatus(result.StandardOutput + Environment.NewLine + result.StandardError);
    }

    public async Task<VmHarnessGuestCommandResult> ExecuteCommandAsync(
        string vmName,
        string command,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Guest transport is not configured. Set TORTOISE_VM_HARNESS_GUEST_USER (and password via secure prompt/env) for PowerShell Direct.");
        }

        var user = Environment.GetEnvironmentVariable("TORTOISE_VM_HARNESS_GUEST_USER")!;
        var password = Environment.GetEnvironmentVariable("TORTOISE_VM_HARNESS_GUEST_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "TORTOISE_VM_HARNESS_GUEST_PASSWORD must be set for non-interactive guest commands.");
        }

        var envSetup = string.Join(
            "; ",
            (environmentVariables ?? new Dictionary<string, string>())
            .Select(pair => $"$env:{pair.Key}='{pair.Value.Replace("'", "''", StringComparison.Ordinal)}'"));

        var escapedVm = vmName.Replace("'", "''", StringComparison.Ordinal);
        var escapedUser = user.Replace("'", "''", StringComparison.Ordinal);
        var escapedPassword = password.Replace("'", "''", StringComparison.Ordinal);
        var escapedCommand = command.Replace("'", "''", StringComparison.Ordinal);

        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $sec = ConvertTo-SecureString '{{escapedPassword}}' -AsPlainText -Force
            $cred = New-Object System.Management.Automation.PSCredential('{{escapedUser}}', $sec)
            $block = {
              {{envSetup}}
              cmd /c "{{escapedCommand}}"
            }
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $output = Invoke-Command -VMName '{{escapedVm}}' -Credential $cred -ScriptBlock $block -ErrorAction Stop 2>&1
            $sw.Stop()
            [pscustomobject]@{
              ExitCode = $LASTEXITCODE
              Output = ($output | Out-String)
              DurationMs = [int]$sw.ElapsedMilliseconds
            } | ConvertTo-Json -Compress
            """;

        var sw = Stopwatch.StartNew();
        var json = await _executor.RunAsync(script, cancellationToken);
        sw.Stop();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        var exitCode = root.TryGetProperty("ExitCode", out var exitProp) ? exitProp.GetInt32() : 1;
        var output = root.TryGetProperty("Output", out var outProp) ? outProp.GetString() ?? string.Empty : json;
        return new VmHarnessGuestCommandResult(exitCode, output, string.Empty, sw.Elapsed);
    }
}

public sealed class UnconfiguredGuestTransport : IVmHarnessGuestTransport
{
    public bool IsConfigured => false;

    public Task<VmHarnessGuestEnvironmentProof> GetEnvironmentProofAsync(
        string vmName,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Guest transport is not configured.");

    public Task<VmHarnessGuestCommandResult> ExecuteCommandAsync(
        string vmName,
        string command,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Guest transport is not configured.");
}
