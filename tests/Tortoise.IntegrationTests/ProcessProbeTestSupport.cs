using System.Diagnostics;
using System.Text;

namespace Tortoise.IntegrationTests;

internal static class ProcessProbeTestSupport
{
    internal static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(15);

    internal static Process StartProbe(string probePath, params string[] args)
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{probePath}\" {string.Join(' ', args)}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Failed to start probe process.");
    }

    internal static async Task WaitForOutputLineAsync(
        Process process,
        string expectedLine,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                if (process.HasExited && process.StandardOutput.Peek() == -1)
                {
                    break;
                }

                var line = await process.StandardOutput.ReadLineAsync(timeoutCts.Token);
                if (line is not null && line.Contains(expectedLine, StringComparison.Ordinal))
                {
                    return;
                }

                if (line is null && process.HasExited)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out after {timeout.TotalSeconds:0}s waiting for probe output containing '{expectedLine}'.");
        }

        var stderr = await ReadAvailableAsync(process.StandardError);
        throw new TimeoutException(
            $"Probe exited without expected output '{expectedLine}'. stderr: {stderr}");
    }

    internal static async Task<string> ReadProcessOutputAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var waitTask = process.WaitForExitAsync(timeoutCts.Token);

        await Task.WhenAll(outputTask, waitTask);
        return await outputTask;
    }

    internal static void EnsureTerminated(Process? process)
    {
        if (process is null || process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort cleanup for test teardown.
        }
    }

    private static async Task<string> ReadAvailableAsync(TextReader reader)
    {
        var builder = new StringBuilder();
        var buffer = new char[256];
        while (reader.Peek() != -1)
        {
            var read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }

            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }
}
