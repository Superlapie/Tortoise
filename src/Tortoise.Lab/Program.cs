using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Versioning;
using Tortoise.Broker.Extensions;
using Tortoise.Broker.Ipc;
using Tortoise.Broker.Validation;
using Tortoise.Contracts.Elevation;
using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
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
        if (!HasJsonFlag(args))
        {
            Console.WriteLine(MutationBuildPolicy.LabBuildNotice);
            Console.WriteLine();
        }

        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "status" => RunStatus(args),
            "vm" => await RunVmAsync(args),
            "broker" => await RunBrokerAsync(args),
            _ => PrintUnknown(args[0]),
        };
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Tortoise.Lab (mutation testing — isolated VM only)");
        Console.WriteLine();
        Console.WriteLine("  tortoise-lab status [--json]");
        Console.WriteLine("  tortoise-lab vm preflight <plan-id> [--json] [--db=path-under-lab-root]");
        Console.WriteLine("  tortoise-lab vm evidence snapshot [--json] [--db=path-under-lab-root]");
        Console.WriteLine("  tortoise-lab vm install <plan-id> [--json] [--db=path-under-lab-root]");
        Console.WriteLine("  tortoise-lab broker serve [--session-id=N] [--pipe=name] [--capability=token] [--db=path-under-lab-root]");
        return 0;
    }

    private static int PrintUnknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
    }

    private static int RunStatus(string[] args)
    {
        IExecutionEnvironmentDetector detector = OperatingSystem.IsWindows()
            ? new WindowsExecutionEnvironmentDetector()
            : new UnsupportedExecutionEnvironmentDetector();

        if (HasJsonFlag(args))
        {
            LabMachineReadableOutput.WriteStatusJson(detector);
            return 0;
        }

        var environment = detector.Detect();
        var capability = MutationCapabilityResolver.Resolve(environment);

        Console.WriteLine($"Lab build: {MutationBuildPolicy.IsLabBuild}");
        Console.WriteLine($"Mutation enabled: {capability.IsEnabled}");
        Console.WriteLine($"Environment: {capability.Environment}");
        Console.WriteLine($"Reason: {capability.Reason}");
        Console.WriteLine($"Disposable VM detected: {environment.IsDisposableVm}");
        Console.WriteLine($"Mutation tests enabled: {environment.MutationTestsEnabled}");
        Console.WriteLine($"VM install explicitly allowed: {environment.VmInstallExplicitlyAllowed}");
        return 0;
    }

    private static async Task<int> RunVmAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: tortoise-lab vm <preflight|evidence|install> ...");
            return 1;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Lab VM commands require Windows.");
            return 1;
        }

        return args[1].ToLowerInvariant() switch
        {
            "preflight" => await RunVmPreflightAsync(args),
            "evidence" => await RunVmEvidenceAsync(args),
            "install" => await RunVmInstallAsync(args),
            _ => PrintUnknown(args[1]),
        };
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> RunVmPreflightAsync(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("VM preflight requires Windows.");
            return 1;
        }

        if (args.Length < 3 || !Guid.TryParse(args[2], out var planId))
        {
            Console.Error.WriteLine("Usage: tortoise-lab vm preflight <plan-id> [--json] [--db=path-under-lab-root]");
            return 1;
        }

        var emitJson = HasJsonFlag(args);
        if (!TryResolveLabDatabasePath(args, out var databasePath, out var databaseError))
        {
            if (emitJson)
            {
                Console.Error.WriteLine(databaseError);
            }
            else
            {
                Console.Error.WriteLine(databaseError);
            }

            return 1;
        }

        var services = CreateLabPlanningServices(databasePath!);
        var provider = services.BuildServiceProvider();
        var scanStore = provider.GetRequiredService<IScanSessionStore>();
        var planService = provider.GetRequiredService<IUpdatePlanService>();
        var preflightService = provider.GetRequiredService<IUpdatePreflightService>();
        var deviceProvider = provider.GetRequiredService<IDeviceInventoryProvider>();
        var environmentDetector = provider.GetRequiredService<IExecutionEnvironmentDetector>();
        await scanStore.InitializeAsync();
        LabAuthoritativeStoreGuard.EnsureProtectedStoreReady(databasePath!);

        var storedPlan = await planService.GetPlanAsync(planId);
        if (storedPlan is null)
        {
            Console.Error.WriteLine($"Plan '{planId}' was not found.");
            return 1;
        }

        var inventory = await deviceProvider.ScanAsync();
        var currentDevice = inventory.Devices.SingleOrDefault(device =>
            string.Equals(
                device.Snapshot.Identity.DeviceInstanceId,
                storedPlan.Plan.DeviceSnapshot.Identity.DeviceInstanceId,
                StringComparison.OrdinalIgnoreCase));

        if (currentDevice is null)
        {
            Console.Error.WriteLine("Target device is no longer present in the current inventory.");
            return 1;
        }

        var capability = MutationCapabilityResolver.Resolve(environmentDetector.Detect());
        var preflight = preflightService.RunPreflight(storedPlan, currentDevice, capability);
        var innerGateEvidence = LabInnerGateEvidenceAssembler.FromPreflight(storedPlan, preflight);

        if (emitJson)
        {
            LabMachineReadableOutput.WritePreflightJson(storedPlan, preflight, innerGateEvidence);
        }
        else
        {
            Console.WriteLine($"Plan: {storedPlan.PlanId}");
            Console.WriteLine($"Blocked: {preflight.IsBlocked}");
            foreach (var check in preflight.Checks)
            {
                Console.WriteLine($"{check.Name}: {check.Result}");
                Console.WriteLine($"  {check.Message}");
            }
        }

        return preflight.IsBlocked ? 2 : 0;
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> RunVmEvidenceAsync(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("VM evidence snapshot requires Windows.");
            return 1;
        }

        if (args.Length < 3 || !string.Equals(args[2], "snapshot", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Usage: tortoise-lab vm evidence snapshot [--json] [--db=path-under-lab-root]");
            return 1;
        }

        var emitJson = HasJsonFlag(args);
        if (!TryResolveLabDatabasePath(args, out var databasePath, out var databaseError))
        {
            Console.Error.WriteLine(databaseError);
            return 1;
        }

        var services = CreateLabPlanningServices(databasePath!);
        var provider = services.BuildServiceProvider();
        var scanStore = provider.GetRequiredService<IScanSessionStore>();
        var planService = provider.GetRequiredService<IUpdatePlanService>();
        var transactionStore = provider.GetRequiredService<IUpdateTransactionStore>();
        var deviceProvider = provider.GetRequiredService<IDeviceInventoryProvider>();
        LabAuthoritativeStoreGuard.EnsureProtectedStoreReady(databasePath!);

        var snapshot = await LabEvidenceSnapshotBuilder.BuildAsync(
            scanStore,
            planService,
            transactionStore,
            deviceProvider);

        if (emitJson)
        {
            LabMachineReadableOutput.WriteEvidenceSnapshotJson(snapshot);
        }
        else
        {
            Console.WriteLine($"Devices: {snapshot.Devices.Count}");
            Console.WriteLine($"Plans: {snapshot.Plans.Count}");
            Console.WriteLine($"Transactions: {snapshot.Transactions.Count}");
        }

        return 0;
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
            Console.Error.WriteLine("Usage: tortoise-lab vm install <plan-id> [--json] [--db=path-under-lab-root]");
            return 1;
        }

        var emitJson = HasJsonFlag(args);
        if (!TryResolveLabDatabasePath(args, out var databasePath, out var databaseError))
        {
            if (emitJson)
            {
                LabMachineReadableOutput.WriteInstallDeniedJson(databaseError ?? "Invalid database path.", planId);
            }
            else
            {
                Console.Error.WriteLine(databaseError);
            }

            return 1;
        }

        var services = new ServiceCollection();
        services.AddTortoisePersistence(options => options.DatabasePath = databasePath);
        services.AddTortoiseVmDriverInstall();
        services.AddTortoiseBrokerDriverInstall();
        services.AddTortoiseLabBrokerElevation();

        var provider = services.BuildServiceProvider();
        var scanStore = provider.GetRequiredService<IScanSessionStore>();
        var planService = provider.GetRequiredService<IUpdatePlanService>();
        var installService = provider.GetRequiredService<IVmDriverInstallService>();
        var transactionStore = provider.GetRequiredService<IUpdateTransactionStore>();
        await scanStore.InitializeAsync();
        LabAuthoritativeStoreGuard.EnsureProtectedStoreReady(databasePath!);

        var storedPlan = await planService.GetPlanAsync(planId);
        if (storedPlan is null)
        {
            if (emitJson)
            {
                LabMachineReadableOutput.WriteInstallDeniedJson($"Plan '{planId}' was not found.", planId);
            }
            else
            {
                Console.Error.WriteLine($"Plan '{planId}' was not found.");
            }

            return 1;
        }

        var brokerHostOptions = CreateBrokerHostOptions(args);
        var brokerOptions = new BrokerPlanValidationOptions(
            brokerHostOptions.SessionId,
            brokerHostOptions.CapabilityToken,
            brokerHostOptions.PipeName,
            brokerHostOptions.ConnectTimeoutMs,
            databasePath,
            Environment.ProcessId);

        try
        {
            var result = await installService.InstallAsync(
                planId,
                new VmDriverInstallOptions(BrokerOptions: brokerOptions));

            var transactionRecord = await transactionStore.GetLatestForPlanAsync(planId);
            var classification = LabMachineReadableOutput.Classify(result, transactionRecord);
            var innerGateEvidence = LabInnerGateEvidenceAssembler.FromInstallAttempt(
                storedPlan,
                result.Preflight,
                transactionRecord,
                result.BrokerInstall,
                result.Summary);

            if (emitJson)
            {
                LabMachineReadableOutput.WriteInstallJson(
                    result,
                    transactionRecord,
                    classification,
                    innerGateEvidence,
                    storedPlan);
            }
            else
            {
                Console.WriteLine(result.Summary);
            }

            return classification switch
            {
                LabInstallClassification.Completed => 0,
                LabInstallClassification.AwaitingReboot => 3,
                LabInstallClassification.PolicyBlocked => 2,
                LabInstallClassification.RecoveryRequired => 4,
                LabInstallClassification.VerificationInconclusive => 4,
                _ => 2,
            };
        }
        catch (MutationDeniedException ex)
        {
            if (emitJson)
            {
                LabMachineReadableOutput.WriteInstallDeniedJson(ex.Reason, planId);
            }
            else
            {
                Console.Error.WriteLine(ex.Reason);
            }

            return 2;
        }
    }

    [SupportedOSPlatform("windows")]
    private static ServiceCollection CreateLabPlanningServices(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddTortoisePersistence(options => options.DatabasePath = databasePath);
        services.AddTortoiseUpdatePlanning();
        services.AddSingleton<IExecutionEnvironmentDetector, WindowsExecutionEnvironmentDetector>();
        return services;
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
        };

        if (!TryResolveLabDatabasePath(args, out var databasePath, out var databaseError))
        {
            Console.Error.WriteLine(databaseError);
            return 1;
        }

        options = options with { DatabasePath = databasePath, AuthorizedClientProcessId = Environment.ProcessId };
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

    private static bool TryResolveLabDatabasePath(string[] args, out string? databasePath, out string? error)
    {
        var requestedPath = ParseStringArg(args, "--db=");
        if (!BrokerDatabasePathValidator.TryValidate(requestedPath, out databasePath, out error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(databasePath))
        {
            error = "Lab database path could not be resolved.";
            return false;
        }

        LabAuthoritativeStoreGuard.EnsureProtectedStoreReady(databasePath);
        return true;
    }

    private static bool HasJsonFlag(string[] args) =>
        args.Any(arg => string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase));

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
