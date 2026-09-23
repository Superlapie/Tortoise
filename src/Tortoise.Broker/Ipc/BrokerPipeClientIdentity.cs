using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Tortoise.Broker.Ipc;

internal static partial class BrokerPipeClientIdentity
{
    internal static bool IsAuthorizedClient(
        PipeStream stream,
        int? authorizedClientProcessId,
        out string? error)
    {
        error = null;

        if (!authorizedClientProcessId.HasValue)
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            if (authorizedClientProcessId.Value != Environment.ProcessId)
            {
                error = "Named pipe client process identity could not be verified on this platform.";
                return false;
            }

            return true;
        }

        if (!TryGetWindowsClientProcessId(stream, out var clientProcessId))
        {
            error = "Named pipe client process identity could not be determined.";
            return false;
        }

        if (clientProcessId != authorizedClientProcessId.Value)
        {
            error =
                $"Named pipe client PID {clientProcessId} does not match authorized Lab client PID {authorizedClientProcessId.Value}.";
            return false;
        }

        return true;
    }

    internal static bool TryGetClientProcessId(PipeStream stream, out int processId)
    {
        if (!OperatingSystem.IsWindows())
        {
            processId = Environment.ProcessId;
            return true;
        }

        return TryGetWindowsClientProcessId(stream, out processId);
    }

    [SupportedOSPlatform("windows")]
    private static bool TryGetWindowsClientProcessId(PipeStream stream, out int processId)
    {
        processId = 0;

        if (stream.SafePipeHandle.IsInvalid)
        {
            return false;
        }

        if (!GetNamedPipeClientProcessId(stream.SafePipeHandle, out var rawProcessId))
        {
            return false;
        }

        processId = unchecked((int)rawProcessId);
        return processId > 0;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);
}
