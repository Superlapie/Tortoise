using Tortoise.Broker.Ipc;
using Tortoise.Broker.Validation;
using Tortoise.Contracts.Mutation;
using Tortoise.Security.Broker;

namespace Tortoise.LabBroker;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "serve", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                "Usage: tortoise-lab-broker serve --session-id=N --client-pid=N [--pipe=name] [--capability=token] [--db=path]");
            return 1;
        }

        if (!MutationBuildPolicy.IsLabBrokerBuild)
        {
            Console.Error.WriteLine("This executable is reserved for Tortoise.Lab mutation testing.");
            return 2;
        }

        if (!TryParseClientProcessId(args, out var clientProcessId))
        {
            Console.Error.WriteLine("Lab broker requires --client-pid=<initiating-lab-process-id>.");
            return 5;
        }

        var sessionId = TryParseSessionId(args, out var parsedSessionId)
            ? parsedSessionId
            : OperatingSystem.IsWindows()
                ? WindowsSessionIdentity.GetCurrentSessionId()
                : Environment.ProcessId;
        var capability = ParseCapability(args) ?? Guid.NewGuid().ToString("N");
        var databasePath = ParseDatabasePath(args);
        if (!BrokerDatabasePathValidator.TryValidate(databasePath, out var normalizedDatabasePath, out var validationError))
        {
            Console.Error.WriteLine(validationError ?? "Broker database path is invalid.");
            return 4;
        }

        if (!LabAuthoritativeStoreGuard.ValidateAuthoritativeStore(normalizedDatabasePath!, out var storeError))
        {
            Console.Error.WriteLine(storeError ?? "Lab authoritative store validation failed.");
            return 6;
        }

        var options = new BrokerHostOptions
        {
            SessionId = sessionId,
            CapabilityToken = capability,
            PipeName = ParsePipeName(args),
            AllowDriverInstall = BrokerHostOptions.ShouldAllowLabBrokerDriverInstall(),
            InstallOnlyMode = true,
            DatabasePath = normalizedDatabasePath,
            AuthorizedClientProcessId = clientProcessId,
        };

        if (!options.AllowDriverInstall)
        {
            Console.Error.WriteLine(
                "Lab broker refused to start: mutation capability is not enabled for this environment.");
            return 3;
        }

        await BrokerHost.RunAuthorizedInstallOnceAsync(options);
        return 0;
    }

    private static bool TryParseClientProcessId(string[] args, out int clientProcessId)
    {
        clientProcessId = 0;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--client-pid=", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(arg["--client-pid=".Length..], out clientProcessId)
                && clientProcessId > 0)
            {
                return true;
            }
        }

        return false;
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

    private static string? ParseDatabasePath(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith("--db=", StringComparison.OrdinalIgnoreCase))
            {
                return arg["--db=".Length..];
            }
        }

        return null;
    }
}
