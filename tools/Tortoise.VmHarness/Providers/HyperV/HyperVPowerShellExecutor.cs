using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Tortoise.VmHarness.Providers.HyperV;

internal sealed class HyperVPowerShellExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<string> RunAsync(string script, CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new VmHarnessProviderException("Failed to start powershell.exe.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new VmHarnessProviderException(
                $"Hyper-V PowerShell command failed (exit {process.ExitCode}): {stderr.Trim()}");
        }

        return stdout.Trim();
    }

    public async Task<T?> RunJsonAsync<T>(string script, CancellationToken cancellationToken)
    {
        var output = await RunAsync(script, cancellationToken);
        if (string.IsNullOrWhiteSpace(output))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(output, JsonOptions);
    }
}

internal sealed record HyperVVmRecord(
    string Id,
    string Name,
    string State);

internal sealed record HyperVSnapshotRecord(
    string Id,
    string Name,
    string CreationTime);
