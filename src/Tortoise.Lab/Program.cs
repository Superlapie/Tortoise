using Microsoft.Extensions.DependencyInjection;
using Tortoise.Broker.Extensions;
using Tortoise.Broker.Ipc;
using Tortoise.Contracts.Elevation;
using Tortoise.Contracts.Mutation;
using Tortoise.Core.Installation;
using Tortoise.Core.Mutation;
using Tortoise.Core.Planning;
using Tortoise.Core.ScanSessions;
using Tortoise.Persistence.Extensions;
using Tortoise.Security.Broker;
using Tortoise.Windows.Extensions;
using Tortoise.WindowsUpdate.Environment;

namespace Tortoise.Lab;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine(MutationBuildPolicy.LabBuildNotice);
        Console.WriteLine();

        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "status" => RunStatus(),
            "vm" => await RunVmAsync(args),
            "broker" => await RunBrokerAsync(args),
            _ => PrintUnknown(args[0]),
        };
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Tortoise.Lab (mutation testing — isolated VM only)");
        Console.WriteLine();
        Console.WriteLine("  tortoise-lab status");
        Console.WriteLine("  tortoise-lab vm install <plan-id> [--db=path]");
        Console.WriteLine("  tortoise-lab broker serve [--session-id=N] [--pipe=name] [--capability=token] [--db=path]");
        return 0;
    }

    private static int PrintUnknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
    }

    private static int RunStatus()
    {
        IExecutionEnvironmentDetector detector = OperatingSystem.IsWindows()
            ? new WindowsExecutionEnvironmentDetector()
            : new UnsupportedExecutionEnvironmentDetector();
        var environment = detector.Detect();
        var capability = MutationCapabilityResolver.Resolve(environment);

        Console.WriteLine($"Lab build: {MutationBuildPolicy.IsLabBuild}");
        Console.WriteLine($"Mutation enabled: {capability.IsEnabled}");
        Console.WriteLine($"Environment: {capability.Environment}");
        Console.WriteLine($"Reason: {capability.Reason}");
        Console.WriteLine($"Disposable VM detected: {environment.IsDisposableVm}");
        return 0;
    }

    private static async Task<int> RunVmAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: tortoise-lab vm install <plan-id>");
            return 1;
        }

        return args[1].ToLowerInvariant() switch
        {
            "install" => await RunVmInstallAsync(args),
            _ => PrintUnknown(args[1]),
        };
    }

    private static async Task<int> RunVmInstallAsync(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("VM install requires Windows.");
            return 1;
        }

        if (args.Length < 3 || !Guid.TryParse(args[2], out var planId))
        {
            Console.Error.WriteLine("Usage: tortoise-lab vm install <plan-id> [--db=path]");
            return 1;
        }

        var services = new ServiceCollection();
        services.AddTortoisePersistence(options => ConfigureDatabasePath(options, args));
        services.AddTortoiseVmDriverInstall();
        services.AddTortoiseBrokerDriverInstall();
        services.AddTortoiseLabBrokerElevation();

        var provider = services.BuildServiceProvider();
        var scanStore = provider.GetRequiredService<IScanSessionStore>();
        var installService = provider.GetRequiredService<IVmDriverInstallService>();
        await scanStore.InitializeAsync();

        var brokerHostOptions = CreateBrokerHostOptions(args);
        var brokerOptions = new BrokerPlanValidationOptions(
            brokerHostOptions.SessionId,
            brokerHostOptions.CapabilityToken,
            brokerHostOptions.PipeName,
            brokerHostOptions.ConnectTimeoutMs,
            ParseStringArg(args, "--db="));

        try
        {
            var result = await installService.InstallAsync(
                planId,
                new VmDriverInstallOptions(BrokerOptions: brokerOptions));

            Console.WriteLine(result.Summary);
            return result.CompletedSuccessfully ? 0 : 2;
        }
        catch (MutationDeniedException ex)
        {
            Console.Error.WriteLine(ex.Reason);
            return 2;
        }
    }

    private static async Task<int> RunBrokerAsync(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[1], "serve", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Usage: tortoise-lab broker serve ...");
            return 1;
        }

        var options = CreateBrokerHostOptions(args) with
        {
            AllowDriverInstall = true,
            InstallOnlyMode = true,
            DatabasePath = ParseStringArg(args, "--db="),
        };
        Console.WriteLine($"Broker listening on pipe '{options.GetEffectivePipeName()}'");
        await BrokerHost.RunAuthorizedInstallOnceAsync(options);
        return 0;
    }

    private static BrokerHostOptions CreateBrokerHostOptions(string[] args)
    {
        var sessionId = ParseIntArg(args, "--session-id=")
            ?? (OperatingSystem.IsWindows() ? WindowsSessionIdentity.GetCurrentSessionId() : Environment.ProcessId);
        var capability = ParseStringArg(args, "--capability=") ?? Guid.NewGuid().ToString("N");
        var pipeName = ParseStringArg(args, "--pipe=");
        return new BrokerHostOptions
        {
            SessionId = sessionId,
            CapabilityToken = capability,
            PipeName = pipeName ?? ElevationConstants.GetPipeName(sessionId, capability),
            AllowDriverInstall = BrokerHostOptions.ShouldAllowDriverInstall(),
        };
    }

    private static void ConfigureDatabasePath(Tortoise.Persistence.Options.TortoisePersistenceOptions options, string[] args)
    {
        var db = ParseStringArg(args, "--db=");
        if (db is not null)
        {
            options.DatabasePath = db;
        }
    }

    private static int? ParseIntArg(string[] args, string prefix)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(arg[prefix.Length..], out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? ParseStringArg(string[] args, string prefix)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[prefix.Length..];
            }
        }

        return null;
    }
}
