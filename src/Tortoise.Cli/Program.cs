using Microsoft.Extensions.DependencyInjection;
using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Diagnostics;
using Tortoise.Core.Drivers;
using Tortoise.Core.FaultInjection;
using Tortoise.Core.Installation;
using Tortoise.Core.Mutation;
using Tortoise.Core.Pilot;
using Tortoise.Core.Planning;
using Tortoise.Core.Recovery;
using Tortoise.Core.Recommendations;
using Tortoise.Core.ScanSessions;
using Tortoise.Core.Updates;
using Tortoise.Contracts.Elevation;
using Tortoise.Persistence.Extensions;
using Tortoise.Security.Broker;
using Tortoise.Broker.Extensions;
using Tortoise.Broker.Ipc;
using Tortoise.Windows.Extensions;
using Tortoise.WindowsUpdate.Environment;
using Tortoise.WindowsUpdate.Extensions;

if (args.Length == 0)
{
    PrintUsage();
    return 0;
}

var command = args[0].ToLowerInvariant();
return command switch
{
    "scan" or "devices" => await RunDeviceScanAsync(args),
    "packages" or "drivers" => await RunPackageScanAsync(args),
    "updates" => await RunUpdatesScanAsync(args),
    "recommend" or "recommendations" => await RunRecommendationsAsync(args),
    "export-report" => await RunExportReportAsync(args),
    "plan" or "plans" => await RunPlansAsync(args),
    "preflight" => await RunPreflightAsync(args),
    "simulate" => await RunSimulateAsync(args),
    "workflow" => await RunWorkflowAsync(args),
    "vm" => await RunVmAsync(args),
    "fault" => await RunFaultAsync(args),
    "pilot" => await RunPilotAsync(args),
    "recover" => await RunRecoverAsync(args),
    "broker" => await RunBrokerAsync(args),
    "status" => RunStatus(),
    _ => PrintUnknown(command),
};

static int PrintUsage()
{
    Console.WriteLine("Tortoise CLI (read-only)");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  tortoise scan");
    Console.WriteLine("  tortoise devices [--problem]");
    Console.WriteLine("  tortoise packages");
    Console.WriteLine("  tortoise updates [--optional]");
    Console.WriteLine("  tortoise recommend [--optional]");
    Console.WriteLine("  tortoise export-report <path> [--session-id=N] [--db=path]");
    Console.WriteLine("  tortoise plan [--session-id=N] [--device=id] [--optional] [--db=path]");
    Console.WriteLine("  tortoise plans [--session-id=N] [--db=path]");
    Console.WriteLine("  tortoise preflight <plan-id> [--db=path]");
    Console.WriteLine("  tortoise simulate <plan-id> [--db=path]");
    Console.WriteLine("  tortoise workflow run <plan-id> [--skip-broker] [--session-id=N] [--pipe=name] [--db=path]");
    Console.WriteLine("  tortoise vm status");
    Console.WriteLine("  tortoise vm install <plan-id> [--skip-broker-validate] [--session-id=N] [--pipe=name] [--db=path]");
    Console.WriteLine("  tortoise fault list");
    Console.WriteLine("  tortoise fault run <scenario> <plan-id> [--db=path]");
    Console.WriteLine("  tortoise fault reconcile <transaction-id> [--db=path]");
    Console.WriteLine("  tortoise pilot checklist <plan-id> [--db=path]");
    Console.WriteLine("  tortoise pilot confirm <plan-id> --phrase=... [--ack=id]... [--db=path]");
    Console.WriteLine("  tortoise pilot recovery-doc [--export=path]");
    Console.WriteLine("  tortoise recover prepare <plan-id> [--output-dir=path] [--db=path]");
    Console.WriteLine("  tortoise recover list [--plan-id=guid] [--db=path]");
    Console.WriteLine("  tortoise recover export <preparation-id> <path> [--db=path]");
    Console.WriteLine("  tortoise broker ping [--session-id=N] [--pipe=name]");
    Console.WriteLine("  tortoise broker status [--session-id=N] [--pipe=name]");
    Console.WriteLine("  tortoise broker serve [--session-id=N] [--pipe=name]");
    Console.WriteLine("  tortoise status");
    return 0;
}

static int PrintUnknown(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return 1;
}

static int RunStatus()
{
    IExecutionEnvironmentDetector detector = OperatingSystem.IsWindows()
        ? new WindowsExecutionEnvironmentDetector()
        : new UnsupportedExecutionEnvironmentDetector();
    var environment = detector.Detect();
    var capability = MutationCapabilityResolver.Resolve(environment);

    Console.WriteLine($"Mutation enabled: {capability.IsEnabled}");
    Console.WriteLine($"Environment: {capability.Environment}");
    Console.WriteLine($"Reason: {capability.Reason}");
    Console.WriteLine($"Lab build: {MutationBuildPolicy.IsLabBuild}");
    Console.WriteLine(MutationBuildPolicy.IsLabBuild
        ? MutationBuildPolicy.LabBuildNotice
        : MutationBuildPolicy.PublicBuildNotice);
    Console.WriteLine($"Disposable VM detected: {environment.IsDisposableVm}");
    Console.WriteLine($"Mutation tests marker: {environment.MutationTestsEnabled}");
    Console.WriteLine($"VM install marker: {environment.VmInstallExplicitlyAllowed}");
    Console.WriteLine($"Physical pilot marker: {environment.PhysicalPilotExplicitlyAllowed}");
    Console.WriteLine($"Platform: {(OperatingSystem.IsWindows() ? "Windows" : "Unsupported for device scan")}");
    return 0;
}

static async Task<int> RunDeviceScanAsync(string[] args)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Device inventory requires Windows.");
        return 1;
    }

    var showProblemsOnly = args.Contains("--problem", StringComparer.OrdinalIgnoreCase);
    var services = new ServiceCollection();
    services.AddTortoiseWindowsDeviceInventory();
    var provider = services.BuildServiceProvider().GetRequiredService<IDeviceInventoryProvider>();
    var result = await provider.ScanAsync();

    var devices = result.Devices.AsEnumerable();
    if (showProblemsOnly)
    {
        devices = devices.Where(entry => entry.Snapshot.Health.State == DeviceHealthState.Problem);
    }

    foreach (var entry in devices.OrderBy(entry => entry.Snapshot.Identity.ClassName))
    {
        var identity = entry.Snapshot.Identity;
        var health = entry.Snapshot.Health;
        Console.WriteLine($"{identity.FriendlyName}");
        Console.WriteLine($"  Class: {identity.ClassName}");
        Console.WriteLine($"  Instance ID: {identity.DeviceInstanceId}");
        Console.WriteLine($"  Status: {health.State}");
        if (health.ProblemCode is not null)
        {
            Console.WriteLine($"  Problem code: {health.ProblemCode}");
        }

        if (entry.InstalledDriver is not null)
        {
            Console.WriteLine($"  Driver: {entry.InstalledDriver.ProviderName} {entry.InstalledDriver.DriverVersion}");
        }

        Console.WriteLine();
    }

    Console.WriteLine($"Devices: {result.Devices.Count}");
    if (result.Warnings.Count > 0)
    {
        Console.WriteLine($"Warnings: {result.Warnings.Count}");
    }

    return 0;
}

static async Task<int> RunPackageScanAsync(string[] args)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Driver package inventory requires Windows.");
        return 1;
    }

    var services = new ServiceCollection();
    services.AddTortoiseWindowsDriverPackageInventory();
    var provider = services.BuildServiceProvider().GetRequiredService<IDriverPackageInventoryProvider>();
    var result = await provider.ScanAsync();

    foreach (var package in result.StorePackages.OrderBy(package => package.ClassName).ThenBy(package => package.ProviderName))
    {
        Console.WriteLine($"{package.PublishedInfName}");
        Console.WriteLine($"  Provider: {package.ProviderName}");
        Console.WriteLine($"  Class: {package.ClassName}");
        Console.WriteLine($"  Version: {package.DriverVersion}");
        Console.WriteLine($"  Inbox: {package.IsInbox}");
        Console.WriteLine();
    }

    Console.WriteLine($"Store packages: {result.StorePackages.Count}");
    Console.WriteLine($"Active associations: {result.Associations.Count}");
    if (result.Warnings.Count > 0)
    {
        Console.WriteLine($"Warnings: {result.Warnings.Count}");
    }

    return 0;
}

static async Task<int> RunUpdatesScanAsync(string[] args)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Windows Update scanning requires Windows.");
        return 1;
    }

    var includeOptional = args.Contains("--optional", StringComparer.OrdinalIgnoreCase);
    var services = new ServiceCollection();
    services.AddTortoiseWindowsUpdate();
    var provider = services.BuildServiceProvider().GetRequiredService<IDriverUpdateProvider>();
    var result = await provider.ScanAsync();

    Console.WriteLine(result.Policy.DisplayMessage);
    Console.WriteLine();

    var candidates = result.Candidates.AsEnumerable();
    if (!includeOptional)
    {
        candidates = candidates.Where(candidate =>
            candidate.Classification is UpdateClassification.WindowsRecommended
                or UpdateClassification.ReviewRequired
                or UpdateClassification.Restricted);
    }

    foreach (var candidate in candidates.OrderBy(candidate => candidate.Classification).ThenBy(candidate => candidate.Title))
    {
        Console.WriteLine(candidate.Title);
        Console.WriteLine($"  Classification: {candidate.Classification}");
        Console.WriteLine($"  Update ID: {candidate.UpdateId}");
        Console.WriteLine($"  Revision: {candidate.Revision}");
        if (!string.IsNullOrWhiteSpace(candidate.DriverManufacturer))
        {
            Console.WriteLine($"  Manufacturer: {candidate.DriverManufacturer}");
        }

        if (candidate.RestartRequired)
        {
            Console.WriteLine("  Restart: Required");
        }

        Console.WriteLine();
    }

    Console.WriteLine($"Candidates: {result.Candidates.Count}");
    if (result.Warnings.Count > 0)
    {
        Console.WriteLine($"Warnings: {result.Warnings.Count}");
    }

    return 0;
}

static async Task<int> RunRecommendationsAsync(string[] args)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Recommendations require Windows.");
        return 1;
    }

    var includeOptional = args.Contains("--optional", StringComparer.OrdinalIgnoreCase);
    var services = new ServiceCollection();
    services.AddTortoiseRecommendations();
    services.AddTortoisePersistence(options => ConfigureDatabasePath(options, args));
    var provider = services.BuildServiceProvider();
    var service = provider.GetRequiredService<IRecommendationScanService>();
    var store = provider.GetRequiredService<IScanSessionStore>();
    await store.InitializeAsync();
    var result = await service.ScanAsync(new RecommendationOptions(IncludeOptionalUpdates: includeOptional));
    await store.SaveAsync(result);

    Console.WriteLine(result.UpdatePolicy.DisplayMessage);
    Console.WriteLine();

    var recommendations = result.Recommendations.AsEnumerable();
    if (!includeOptional)
    {
        recommendations = recommendations.Where(recommendation =>
            recommendation.Classification != UpdateClassification.WindowsOptional);
    }

    foreach (var recommendation in recommendations
                 .OrderBy(entry => entry.Classification)
                 .ThenBy(entry => entry.Device.Snapshot.Identity.FriendlyName))
    {
        var device = recommendation.Device.Snapshot.Identity;
        Console.WriteLine(device.FriendlyName);
        Console.WriteLine($"  Class: {device.ClassName}");
        Console.WriteLine($"  Status: {recommendation.Summary}");
        Console.WriteLine($"  Risk: {recommendation.RiskLevel}");
        Console.WriteLine($"  Why: {recommendation.Explanation}");

        if (recommendation.ApplicableUpdate is not null)
        {
            Console.WriteLine($"  Package: {recommendation.ApplicableUpdate.Title}");
        }

        Console.WriteLine();
    }

    var actionable = result.Recommendations.Count(recommendation =>
        recommendation.Classification is UpdateClassification.WindowsRecommended
            or UpdateClassification.WindowsOptional
            or UpdateClassification.ReviewRequired);

    Console.WriteLine($"Devices evaluated: {result.Recommendations.Count}");
    Console.WriteLine($"Actionable recommendations: {actionable.ToString()}");
    Console.WriteLine($"Unmatched Windows Update packages: {result.UnmatchedUpdates.Count}");
    if (result.Warnings.Count > 0)
    {
        Console.WriteLine($"Warnings: {result.Warnings.Count}");
    }

    return 0;
}

static async Task<int> RunPlansAsync(string[] args)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Update planning requires Windows.");
        return 1;
    }

    var isListOnly = string.Equals(args[0], "plans", StringComparison.OrdinalIgnoreCase);
    var services = new ServiceCollection();
    services.AddTortoisePersistence(options => ConfigureDatabasePath(options, args));
    services.AddTortoiseUpdatePlanning();
    var provider = services.BuildServiceProvider();
    var scanStore = provider.GetRequiredService<IScanSessionStore>();
    var planService = provider.GetRequiredService<IUpdatePlanService>();

    await scanStore.InitializeAsync();

    if (isListOnly)
    {
        var existing = await planService.ListPlansAsync(ParseSessionId(args));
        if (existing.Count == 0)
        {
            Console.WriteLine("No update plans found.");
            return 0;
        }

        foreach (var plan in existing)
        {
            PrintPlanSummary(plan);
        }

        Console.WriteLine($"Plans: {existing.Count.ToString()}");
        return 0;
    }

    var options = new UpdatePlanningOptions(
        IncludeOptionalUpdates: args.Contains("--optional", StringComparer.OrdinalIgnoreCase),
        DeviceInstanceId: ParseDeviceInstanceId(args),
        ScanSessionId: ParseSessionId(args));

    var plans = options.ScanSessionId is null
        ? await planService.CreatePlansFromLatestScanAsync(options)
        : await planService.CreatePlansFromScanSessionAsync(options.ScanSessionId.Value, options);

    if (plans.Count == 0)
    {
        Console.WriteLine("No actionable update plans were created.");
        return 0;
    }

    foreach (var plan in plans)
    {
        PrintPlanSummary(plan);
    }

    Console.WriteLine($"Plans created: {plans.Count.ToString()}");
    return 0;
}

static async Task<int> RunPreflightAsync(string[] args)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Preflight requires Windows.");
        return 1;
    }

    if (args.Length < 2 || !Guid.TryParse(args[1], out var planId))
    {
        Console.Error.WriteLine("Usage: tortoise preflight <plan-id> [--db=path]");
        return 1;
    }

    var services = new ServiceCollection();
    services.AddTortoisePersistence(options => ConfigureDatabasePath(options, args));
    services.AddTortoiseUpdatePlanning();
    var provider = services.BuildServiceProvider();
    var planService = provider.GetRequiredService<IUpdatePlanService>();
    var preflightService = provider.GetRequiredService<IUpdatePreflightService>();
    var deviceProvider = provider.GetRequiredService<IDeviceInventoryProvider>();

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

    var preflight = preflightService.RunPreflight(
        storedPlan,
        currentDevice,
        MutationCapability.ReadOnly);

    Console.WriteLine($"Plan: {storedPlan.PlanId}");
    Console.WriteLine($"Blocked: {preflight.IsBlocked}");
    Console.WriteLine();

    foreach (var check in preflight.Checks)
    {
        Console.WriteLine($"{check.Name}: {check.Result}");
        Console.WriteLine($"  {check.Message}");
    }

    return preflight.IsBlocked ? 2 : 0;
}

static async Task<int> RunSimulateAsync(string[] args)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Simulation requires Windows.");
        return 1;
    }

    if (args.Length < 2 || !Guid.TryParse(args[1], out var planId))
    {
        Console.Error.WriteLine("Usage: tortoise simulate <plan-id> [--db=path]");
        return 1;
    }

    var services = new ServiceCollection();
    services.AddTortoisePersistence(options => ConfigureDatabasePath(options, args));
    services.AddTortoiseUpdatePlanning();
    var provider = services.BuildServiceProvider();
    var simulator = provider.GetRequiredService<ISimulatedUpdateTransactionService>();
    var scanStore = provider.GetRequiredService<IScanSessionStore>();
    await scanStore.InitializeAsync();

    var result = await simulator.SimulateAsync(planId, MutationCapability.ReadOnly);

    Console.WriteLine(result.Summary);
    Console.WriteLine($"Transaction: {result.Transaction.Transaction.TransactionId}");
    Console.WriteLine($"State: {result.Transaction.Transaction.State}");
    Console.WriteLine();

    foreach (var entry in result.Transaction.Journal)
    {
        Console.WriteLine($"{entry.FromState} -> {entry.ToState}: {entry.Message}");
    }

    return result.ReachedAwaitingConsent ? 0 : 2;
}

static async Task<int> RunVmAsync(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: tortoise vm <status|install> ...");
        return 1;
    }

    return args[1].ToLowerInvariant() switch
    {
        "status" => RunVmStatus(),
        "install" => await RunVmInstallAsync(args),
        _ => PrintUnknown(args[1]),
    };
}

static int RunVmStatus()
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("VM status requires Windows.");
        return 1;
    }

    return RunStatus();
}

static async Task<int> RunVmInstallAsync(string[] args)
{
    if (!MutationBuildPolicy.AllowsRealMutation)
    {
        Console.Error.WriteLine("Real VM driver install is only available in Tortoise.Lab (tortoise-lab).");
        Console.Error.WriteLine(MutationBuildPolicy.PublicBuildNotice);
        return 2;
    }

    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("VM install requires Windows.");
        return 1;
    }

    if (args.Length < 3 || !Guid.TryParse(args[2], out var planId))
    {
        Console.Error.WriteLine("Usage: tortoise vm install <plan-id> [--skip-broker-validate] [--session-id=N] [--pipe=name] [--db=path]");
        return 1;
    }

    var skipBrokerValidate = args.Contains("--skip-broker-validate", StringComparer.OrdinalIgnoreCase);
    var services = new ServiceCollection();
    services.AddTortoisePersistence(options => ConfigureDatabasePath(options, args));
    services.AddTortoiseVmDriverInstall();
    services.AddTortoiseBrokerPlanValidation();
    services.AddTortoiseBrokerDriverInstall();

    var provider = services.BuildServiceProvider();
    var scanStore = provider.GetRequiredService<IScanSessionStore>();
    var installService = provider.GetRequiredService<IVmDriverInstallService>();
    await scanStore.InitializeAsync();

    var brokerHostOptions = CreateBrokerHostOptions(args);
    await StartBrokerServerIfNeededAsync(brokerHostOptions with { AllowDriverInstall = true });
    var brokerOptions = new BrokerPlanValidationOptions(
        brokerHostOptions.SessionId,
        brokerHostOptions.CapabilityToken,
        brokerHostOptions.PipeName,
        brokerHostOptions.ConnectTimeoutMs);

    try
    {
        var result = await installService.InstallAsync(
            planId,
            new VmDriverInstallOptions(
                RequireBrokerValidation: !skipBrokerValidate,
                BrokerOptions: brokerOptions));

        Console.WriteLine(result.Summary);
        Console.WriteLine($"Plan: {result.PlanId}");
        Console.WriteLine($"Preflight blocked: {result.Preflight.IsBlocked}");
        Console.WriteLine($"Post-install verification: {result.PostInstallVerification.Result}");

        if (result.BrokerValidation is not null)
        {
            Console.WriteLine($"Broker validation: {(result.BrokerValidation.Succeeded ? "Succeeded" : "Failed")}");
        }

        if (result.BrokerInstall is not null)
        {
            Console.WriteLine($"Broker install result code: {result.BrokerInstall.ResultCode}");
            Console.WriteLine($"Reboot required: {result.BrokerInstall.RebootRequired}");
        }

        return result.CompletedSuccessfully ? 0 : 2;
    }
    catch (MutationDeniedException ex)
    {
        Console.Error.WriteLine(ex.Reason);
        return 2;
    }
}

static async Task<int> RunPilotAsync(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: tortoise pilot <checklist|confirm|recovery-doc> ...");
        return 1;
    }

    return args[1].ToLowerInvariant() switch
    {
        "checklist" => await RunPilotChecklistAsync(args),
        "confirm" => await RunPilotConfirmAsync(args),
        "recovery-doc" => await RunPilotRecoveryDocAsync(args),
        _ => PrintUnknown(args[1]),
    };
}

static async Task<int> RunPilotChecklistAsync(string[] args)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Physical pilot checklist requires Windows.");
        return 1;
    }

    if (args.Length < 3 || !Guid.TryParse(args[2], out var planId))
    {
        Console.Error.WriteLine("Usage: tortoise pilot checklist <plan-id> [--db=path]");
        return 1;
    }

    var services = new ServiceCollection();
    services.AddTortoisePersistence(options => ConfigureDatabasePath(options, args));
    services.AddTortoisePhysicalPilotReadiness();
    var provider = services.BuildServiceProvider();
    var scanStore = provider.GetRequiredService<IScanSessionStore>();
    var checklistService = provider.GetRequiredService<IPhysicalPilotChecklistService>();
    await scanStore.InitializeAsync();

    var result = await checklistService.EvaluateAsync(planId);

    Console.WriteLine($"Plan: {result.PlanId}");
    Console.WriteLine($"Ready: {result.IsReady}");
    Console.WriteLine(result.Summary);
    Console.WriteLine();

    foreach (var item in result.Items)
    {
        Console.WriteLine($"{item.Title}: {item.Status}");
        Console.WriteLine($"  {item.Detail}");
    }

    return result.IsReady ? 0 : 2;
}

static async Task<int> RunPilotConfirmAsync(string[] args)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Physical pilot confirmation requires Windows.");
        return 1;
    }

    if (args.Length < 3 || !Guid.TryParse(args[2], out var planId))
    {
        Console.Error.WriteLine("Usage: tortoise pilot confirm <plan-id> --phrase=... [--ack=id]... [--db=path]");
        return 1;
    }

    var phrase = ParseConfirmationPhrase(args);
    if (phrase is null)
    {
        Console.Error.WriteLine("Missing required --phrase= argument.");
        return 1;
    }

    var services = new ServiceCollection();
    services.AddTortoisePersistence(options => ConfigureDatabasePath(options, args));
    services.AddTortoisePhysicalPilotReadiness();
    var provider = services.BuildServiceProvider();
    var scanStore = provider.GetRequiredService<IScanSessionStore>();
    var confirmationService = provider.GetRequiredService<IPhysicalPilotConfirmationService>();
    await scanStore.InitializeAsync();

    var result = await confirmationService.ConfirmAsync(
        new PhysicalPilotConfirmationRequest(
            planId,
            phrase,
            ParseAcknowledgementIds(args)));

    Console.WriteLine(result.Summary);
    Console.WriteLine($"Accepted: {result.Accepted}");
    Console.WriteLine($"Confirmation: {result.Record.ConfirmationId}");
    Console.WriteLine($"Checklist ready: {result.Record.ChecklistReady}");

    return result.Accepted ? 0 : 2;
}

static async Task<int> RunPilotRecoveryDocAsync(string[] args)
{
    var services = new ServiceCollection();
    services.AddTortoisePhysicalPilotReadiness();
    var guide = services.BuildServiceProvider().GetRequiredService<IPhysicalPilotRecoveryGuide>();
    var markdown = guide.GetMarkdown();
    var exportPath = ParseExportPath(args);

    if (exportPath is not null)
    {
        await guide.ExportAsync(exportPath);
        Console.WriteLine($"Exported physical pilot recovery guide to {exportPath}");
        return 0;
    }

    Console.WriteLine(markdown);
    return 0;
}

static async Task<int> RunFaultAsync(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: tortoise fault <list|run|reconcile> ...");
        return 1;
    }

    return args[1].ToLowerInvariant() switch
    {
        "list" => RunFaultList(),
        "run" => await RunFaultRunAsync(args),
        "reconcile" => await RunFaultReconcileAsync(args),
        _ => PrintUnknown(args[1]),
    };
}

static int RunFaultList()
{
    Console.WriteLine("Fault injection scenarios (require TORTOISE_FAULT_INJECTION=1):");
    Console.WriteLine();

    foreach (var definition in FaultInjectionScenarioCatalog.All)
    {
        Console.WriteLine(definition.Scenario);
        Console.WriteLine($"  {definition.Title}");
        Console.WriteLine($"  Checkpoint: {definition.Checkpoint}");
        Console.WriteLine($"  {definition.Description}");
        Console.WriteLine();
    }

    return 0;
}

static async Task<int> RunFaultRunAsync(string[] args)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Fault injection requires Windows.");
        return 1;
    }

    if (args.Length < 4 || !Guid.TryParse(args[3], out var planId))
    {
        Console.Error.WriteLine("Usage: tortoise fault run <scenario> <plan-id> [--db=path]");
        return 1;
    }

    if (!FaultInjectionScenarioCatalog.TryParse(args[2], out var scenario))
    {
        Console.Error.WriteLine($"Unknown fault scenario: {args[2]}");
        RunFaultList();
        return 1;
    }

    if (!FaultInjectionGate.IsEnabled())
    {
        Console.Error.WriteLine("Fault injection is disabled. Set TORTOISE_FAULT_INJECTION=1 first.");
        return 2;
    }

    var services = new ServiceCollection();
    services.AddTortoisePersistence(options => ConfigureDatabasePath(options, args));
    services.AddTortoiseFaultInjection();
    var provider = services.BuildServiceProvider();
    var scanStore = provider.GetRequiredService<IScanSessionStore>();
    var workflow = provider.GetRequiredService<IFaultInjectionWorkflowService>();
    await scanStore.InitializeAsync();

    var result = await workflow.RunScenarioAsync(planId, scenario);

    Console.WriteLine(result.Summary);
    Console.WriteLine($"Transaction: {result.Transaction.Transaction.TransactionId}");
    Console.WriteLine($"State: {result.Transaction.Transaction.State}");
    Console.WriteLine($"Scenario: {result.InjectedFault.Scenario}");
    Console.WriteLine($"Requires recovery: {result.InjectedFault.RequiresRecovery}");
    Console.WriteLine($"Guidance: {result.InjectedFault.RecoveryGuidance}");
    Console.WriteLine();
    Console.WriteLine("Reconcile with:");
    Console.WriteLine($"  tortoise fault reconcile {result.Transaction.Transaction.TransactionId}");

    return 0;
}

static async Task<int> RunFaultReconcileAsync(string[] args)
{
    if (args.Length < 3 || !Guid.TryParse(args[2], out var transactionId))
    {
        Console.Error.WriteLine("Usage: tortoise fault reconcile <transaction-id> [--db=path]");
        return 1;
    }

    var services = new ServiceCollection();
    services.AddTortoisePersistence(options => ConfigureDatabasePath(options, args));
    services.AddTortoiseFaultInjection();
    var provider = services.BuildServiceProvider();
    var reconciliation = provider.GetRequiredService<IFaultReconciliationService>();

    var result = await reconciliation.ReconcileAsync(transactionId);

    Console.WriteLine(result.Summary);
    Console.WriteLine($"Transaction: {result.TransactionId}");
    Console.WriteLine($"Plan: {result.PlanId}");
    Console.WriteLine($"Last state: {result.LastState}");
    Console.WriteLine($"Injected scenario: {result.InjectedScenario?.ToString() ?? "none"}");
    Console.WriteLine($"Recommended action: {result.RecommendedAction}");
    Console.WriteLine($"Guidance: {result.Guidance}");

    return 0;
}

static async Task<int> RunWorkflowAsync(string[] args)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Simulated mutation workflow requires Windows.");
        return 1;
    }

    if (args.Length < 3
        || !string.Equals(args[1], "run", StringComparison.OrdinalIgnoreCase)
        || !Guid.TryParse(args[2], out var planId))
    {
        Console.Error.WriteLine("Usage: tortoise workflow run <plan-id> [--skip-broker] [--session-id=N] [--pipe=name] [--db=path]");
        return 1;
    }

    var skipBroker = args.Contains("--skip-broker", StringComparer.OrdinalIgnoreCase);
    var services = new ServiceCollection();
    services.AddTortoisePersistence(options => ConfigureDatabasePath(options, args));
    services.AddTortoiseSimulatedMutationWorkflow();
    if (!skipBroker)
    {
        services.AddTortoiseBrokerPlanValidation();
    }

    var provider = services.BuildServiceProvider();
    var scanStore = provider.GetRequiredService<IScanSessionStore>();
    var workflow = provider.GetRequiredService<ISimulatedMutationWorkflowService>();
    await scanStore.InitializeAsync();

    BrokerPlanValidationOptions? brokerOptions = null;
    if (!skipBroker)
    {
        var brokerHostOptions = CreateBrokerHostOptions(args);
        await StartBrokerServerIfNeededAsync(brokerHostOptions);
        brokerOptions = new BrokerPlanValidationOptions(
            brokerHostOptions.SessionId,
            brokerHostOptions.CapabilityToken,
            brokerHostOptions.PipeName,
            brokerHostOptions.ConnectTimeoutMs);
    }

    var result = await workflow.RunAsync(
        planId,
        new SimulatedMutationWorkflowOptions(
            RequireBrokerValidation: !skipBroker,
            BrokerOptions: brokerOptions));

    Console.WriteLine(result.Summary);
    Console.WriteLine($"Transaction: {result.Transaction.Transaction.TransactionId}");
    Console.WriteLine($"State: {result.Transaction.Transaction.State}");
    Console.WriteLine($"Package verification: {result.PackageVerification.Result}");
    Console.WriteLine($"Post-install verification: {result.PostInstallVerification.Result}");

    if (result.BrokerValidation is not null)
    {
        Console.WriteLine($"Broker validation: {(result.BrokerValidation.Succeeded ? "Succeeded" : "Failed")}");
        Console.WriteLine($"  {result.BrokerValidation.Message}");
    }

    Console.WriteLine();
    foreach (var entry in result.Transaction.Journal)
    {
        Console.WriteLine($"{entry.FromState} -> {entry.ToState}: {entry.Message}");
    }

    return result.CompletedSuccessfully ? 0 : 2;
}

static void PrintPlanSummary(StoredUpdatePlan plan)
{
    Console.WriteLine(plan.PlanId.ToString());
    Console.WriteLine($"  Device: {plan.Plan.DeviceSnapshot.Identity.FriendlyName}");
    Console.WriteLine($"  Instance ID: {plan.Plan.DeviceSnapshot.Identity.DeviceInstanceId}");
    Console.WriteLine($"  Classification: {plan.Classification}");
    Console.WriteLine($"  Risk: {plan.RiskLevel}");
    Console.WriteLine($"  Frozen: {plan.IsFrozen}");
    Console.WriteLine($"  Proposed: {plan.Plan.ProposedUpdate.Candidate.Package.Identity.PublishedInfName}");
    Console.WriteLine($"  Plan hash: {plan.Plan.PlanHash}");
    Console.WriteLine();
}

static string? ParseDeviceInstanceId(string[] args)
{
    foreach (var arg in args)
    {
        if (arg.StartsWith("--device=", StringComparison.OrdinalIgnoreCase))
        {
            return arg["--device=".Length..];
        }
    }

    return null;
}

static string? ParseOutputDirectory(string[] args)
{
    foreach (var arg in args)
    {
        if (arg.StartsWith("--output-dir=", StringComparison.OrdinalIgnoreCase))
        {
            return arg["--output-dir=".Length..];
        }
    }

    return null;
}

static string? ParseExportPath(string[] args)
{
    foreach (var arg in args)
    {
        if (arg.StartsWith("--export=", StringComparison.OrdinalIgnoreCase))
        {
            return arg["--export=".Length..];
        }
    }

    return null;
}

static string? ParseConfirmationPhrase(string[] args)
{
    foreach (var arg in args)
    {
        if (arg.StartsWith("--phrase=", StringComparison.OrdinalIgnoreCase))
        {
            return arg["--phrase=".Length..];
        }
    }

    return null;
}

static IReadOnlyList<string> ParseAcknowledgementIds(string[] args)
{
    var ids = new List<string>();
    foreach (var arg in args)
    {
        if (arg.StartsWith("--ack=", StringComparison.OrdinalIgnoreCase))
        {
            ids.Add(arg["--ack=".Length..]);
        }
    }

    return ids;
}

static Guid? ParsePlanGuid(string[] args)
{
    foreach (var arg in args)
    {
        if (arg.StartsWith("--plan-id=", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(arg["--plan-id=".Length..], out var planId))
        {
            return planId;
        }
    }

    return null;
}

static async Task<int> RunRecoverAsync(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: tortoise recover <prepare|list|export> ...");
        return 1;
    }

    return args[1].ToLowerInvariant() switch
    {
        "prepare" => await RunRecoverPrepareAsync(args),
        "list" => await RunRecoverListAsync(args),
        "export" => await RunRecoverExportAsync(args),
        _ => PrintUnknown(args[1]),
    };
}

static async Task<int> RunRecoverPrepareAsync(string[] args)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Recovery preparation requires Windows.");
        return 1;
    }

    if (args.Length < 3 || !Guid.TryParse(args[2], out var planId))
    {
        Console.Error.WriteLine("Usage: tortoise recover prepare <plan-id> [--output-dir=path] [--db=path]");
        return 1;
    }

    var services = new ServiceCollection();
    services.AddTortoisePersistence(options => ConfigureDatabasePath(options, args));
    services.AddTortoiseRecoveryPreparation();
    var provider = services.BuildServiceProvider();
    var scanStore = provider.GetRequiredService<IScanSessionStore>();
    var recoveryService = provider.GetRequiredService<IRecoveryPreparationService>();

    await scanStore.InitializeAsync();
    var record = await recoveryService.PrepareAsync(planId, ParseOutputDirectory(args));

    Console.WriteLine($"Preparation: {record.PreparationId}");
    Console.WriteLine($"Plan: {record.PlanId}");
    Console.WriteLine($"Device: {record.Snapshot.BeforeDevice.Identity.FriendlyName}");
    Console.WriteLine($"Export enabled: {record.ExportAttempt.ExportServiceEnabled}");
    Console.WriteLine($"Export succeeded: {record.ExportAttempt.Succeeded}");
    Console.WriteLine($"Export message: {record.ExportAttempt.Message}");
    Console.WriteLine($"System Restore enabled: {record.SystemRestore.IsEnabled}");
    return 0;
}

static async Task<int> RunRecoverListAsync(string[] args)
{
    var services = new ServiceCollection();
    services.AddTortoisePersistence(options => ConfigureDatabasePath(options, args));
    var provider = services.BuildServiceProvider();
    var store = provider.GetRequiredService<IRecoveryPreparationStore>();
    var summaries = await store.ListAsync(ParsePlanGuid(args));

    if (summaries.Count == 0)
    {
        Console.WriteLine("No recovery preparations found.");
        return 0;
    }

    foreach (var summary in summaries)
    {
        Console.WriteLine(summary.PreparationId);
        Console.WriteLine($"  Plan: {summary.PlanId}");
        Console.WriteLine($"  Device: {summary.DeviceName}");
        Console.WriteLine($"  Prepared: {summary.PreparedAtUtc:G}");
        Console.WriteLine($"  Export succeeded: {summary.ExportSucceeded}");
        Console.WriteLine($"  System Restore enabled: {summary.SystemRestoreEnabled}");
        Console.WriteLine();
    }

    Console.WriteLine($"Preparations: {summaries.Count.ToString()}");
    return 0;
}

static async Task<int> RunRecoverExportAsync(string[] args)
{
    if (args.Length < 4 || !Guid.TryParse(args[2], out var preparationId))
    {
        Console.Error.WriteLine("Usage: tortoise recover export <preparation-id> <path> [--db=path]");
        return 1;
    }

    var outputPath = args[3];
    var services = new ServiceCollection();
    services.AddTortoisePersistence(options => ConfigureDatabasePath(options, args));
    var provider = services.BuildServiceProvider();
    var store = provider.GetRequiredService<IRecoveryPreparationStore>();
    var exporter = provider.GetRequiredService<IRecoveryManifestExporter>();

    var record = await store.GetAsync(preparationId);
    if (record is null)
    {
        Console.Error.WriteLine($"Recovery preparation '{preparationId}' was not found.");
        return 1;
    }

    await exporter.ExportAsync(record, outputPath);
    Console.WriteLine($"Exported recovery manifest for {preparationId} to {outputPath}");
    return 0;
}

static async Task<int> RunExportReportAsync(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: tortoise export-report <path> [--session-id=N] [--db=path]");
        return 1;
    }

    var outputPath = args[1];
    var sessionId = ParseSessionId(args);
    var services = new ServiceCollection();
    services.AddTortoisePersistence(options => ConfigureDatabasePath(options, args));
    var provider = services.BuildServiceProvider();
    var store = provider.GetRequiredService<IScanSessionStore>();
    var exporter = provider.GetRequiredService<IDiagnosticsReportExporter>();

    await store.InitializeAsync();

    if (sessionId is null)
    {
        await exporter.ExportLatestSessionAsync(outputPath);
        var latest = await store.GetLatestAsync();
        Console.WriteLine($"Exported latest scan session {latest!.Id.ToString()} to {outputPath}");
    }
    else
    {
        await exporter.ExportSessionAsync(sessionId.Value, outputPath);
        Console.WriteLine($"Exported scan session {sessionId.Value.ToString()} to {outputPath}");
    }

    return 0;
}

static async Task<int> RunBrokerAsync(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: tortoise broker <ping|status|serve> [--session-id=N] [--pipe=name]");
        return 1;
    }

    return args[1].ToLowerInvariant() switch
    {
        "ping" => await RunBrokerPingAsync(args),
        "status" => await RunBrokerStatusAsync(args),
        "serve" => await RunBrokerServeAsync(args),
        _ => PrintUnknown(args[1]),
    };
}

static async Task<int> RunBrokerPingAsync(string[] args)
{
    var options = CreateBrokerHostOptions(args);
    var client = new BrokerPipeClient();

    await StartBrokerServerIfNeededAsync(options);
    var response = await client.SendAsync(
        options,
        BrokerRequestFactory.Create(BrokerOperation.Ping, options.SessionId, options.CapabilityToken));

    Console.WriteLine(response.Message);
    return response.Succeeded ? 0 : 2;
}

static async Task<int> RunBrokerStatusAsync(string[] args)
{
    var options = CreateBrokerHostOptions(args);
    var client = new BrokerPipeClient();

    await StartBrokerServerIfNeededAsync(options);
    var response = await client.SendAsync(
        options,
        BrokerRequestFactory.Create(BrokerOperation.GetStatus, options.SessionId, options.CapabilityToken));

    Console.WriteLine(response.Message);
    if (!string.IsNullOrWhiteSpace(response.PayloadJson))
    {
        Console.WriteLine(response.PayloadJson);
    }

    return response.Succeeded ? 0 : 2;
}

static async Task<int> RunBrokerServeAsync(string[] args)
{
    var options = CreateBrokerHostOptions(args);
    Console.WriteLine($"Broker listening on pipe '{options.GetEffectivePipeName()}' for Windows session {options.SessionId.ToString()}");
    await BrokerHost.RunOnceAsync(options);
    Console.WriteLine("Broker request handled. Exiting.");
    return 0;
}

static BrokerHostOptions CreateBrokerHostOptions(string[] args)
{
    var sessionId = ParseBrokerSessionId(args) ?? WindowsSessionIdentity.GetCurrentSessionId();
    var capability = ParseBrokerCapability(args) ?? Guid.NewGuid().ToString("N");
    var pipeName = ParseBrokerPipeName(args);
    return new BrokerHostOptions
    {
        SessionId = sessionId,
        CapabilityToken = capability,
        PipeName = pipeName ?? ElevationConstants.GetPipeName(sessionId, capability),
        AllowDriverInstall = BrokerHostOptions.ShouldAllowDriverInstall(),
    };
}

static int? ParseBrokerSessionId(string[] args)
{
    foreach (var arg in args)
    {
        if (arg.StartsWith("--session-id=", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(arg["--session-id=".Length..], out var sessionId))
        {
            return sessionId;
        }
    }

    return null;
}

static string? ParseBrokerPipeName(string[] args)
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

static string? ParseBrokerCapability(string[] args)
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

static async Task StartBrokerServerIfNeededAsync(BrokerHostOptions options)
{
    var client = new BrokerPipeClient();
    try
    {
        _ = await client.SendAsync(
            options with { ConnectTimeoutMs = 250 },
            BrokerRequestFactory.Create(BrokerOperation.Ping, options.SessionId, options.CapabilityToken));
        return;
    }
    catch
    {
        // Broker is not already running; start a one-shot server in the background.
    }

    _ = Task.Run(() => BrokerHost.RunOnceAsync(options));
    await Task.Delay(150);
}

static long? ParseSessionId(string[] args)
{
    foreach (var arg in args)
    {
        if (arg.StartsWith("--session-id=", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(arg["--session-id=".Length..], out var sessionId))
        {
            return sessionId;
        }
    }

    return null;
}

static void ConfigureDatabasePath(Tortoise.Persistence.Options.TortoisePersistenceOptions options, string[] args)
{
    foreach (var arg in args)
    {
        if (arg.StartsWith("--db=", StringComparison.OrdinalIgnoreCase))
        {
            options.DatabasePath = arg["--db=".Length..];
        }
    }
}
