using Tortoise.Broker.Ipc;

namespace Tortoise.Broker;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "serve", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Usage: Tortoise.Broker serve --session-id=N [--pipe=name]");
            return 1;
        }

        if (!TryParseSessionId(args, out var sessionId))
        {
            Console.Error.WriteLine("Missing required --session-id=N argument.");
            return 1;
        }

        var options = new BrokerHostOptions
        {
            SessionId = sessionId,
            PipeName = ParsePipeName(args),
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
}
