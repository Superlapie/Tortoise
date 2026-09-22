using Tortoise.Broker.Ipc;
using Tortoise.Security.Broker;

namespace Tortoise.Broker;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "serve", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Usage: Tortoise.Broker serve --session-id=N [--pipe=name] [--capability=token]");
            return 1;
        }

        var sessionId = TryParseSessionId(args, out var parsedSessionId)
            ? parsedSessionId
            : OperatingSystem.IsWindows()
                ? WindowsSessionIdentity.GetCurrentSessionId()
                : Environment.ProcessId;
        var capability = ParseCapability(args) ?? Guid.NewGuid().ToString("N");
        var options = new BrokerHostOptions
        {
            SessionId = sessionId,
            CapabilityToken = capability,
            PipeName = ParsePipeName(args),
            AllowDriverInstall = BrokerHostOptions.ShouldAllowDriverInstall(),
        };

        await BrokerHost.RunOnceAsync(options);
        return 0;
    }

    private static bool TryParseSessionId(string[] args, out int sessionId)
    {
        sessionId = 0;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--session-id=", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(arg["--session-id=".Length..], out sessionId))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ParsePipeName(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith("--pipe=", StringComparison.OrdinalIgnoreCase))
            {
                return arg["--pipe=".Length..];
            }
        }

        return null;
    }

    private static string? ParseCapability(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith("--capability=", StringComparison.OrdinalIgnoreCase))
            {
                return arg["--capability=".Length..];
            }
        }

        return null;
    }
}
