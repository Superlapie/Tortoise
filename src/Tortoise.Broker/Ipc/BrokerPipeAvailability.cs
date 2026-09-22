using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tortoise.Broker.Ipc;

public static partial class BrokerPipeAvailability
{
    public static async Task WaitUntilReadyAsync(
        string pipeName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Named pipe readiness checks require Windows.");
        }

        var fullPipeName = pipeName.StartsWith(@"\\.\pipe\", StringComparison.Ordinal)
            ? pipeName
            : $@"\\.\pipe\{pipeName}";

        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (WaitNamedPipe(fullPipeName, 250))
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new TimeoutException($"Broker pipe '{pipeName}' did not become ready within {timeout.TotalSeconds:0.#}s.");
    }

    [SupportedOSPlatform("windows")]
    [LibraryImport("kernel32.dll", EntryPoint = "WaitNamedPipeW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WaitNamedPipe(string pipeName, uint timeoutMs);
}
