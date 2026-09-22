using Microsoft.Extensions.DependencyInjection;
using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Diagnostics;
using Tortoise.Core.Drivers;
using Tortoise.Core.Planning;
using Tortoise.Core.Recommendations;
using Tortoise.Core.ScanSessions;
using Tortoise.Core.Updates;
using Tortoise.Persistence.Extensions;
using Tortoise.Windows.Extensions;
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
    Console.WriteLine($"Mutation enabled: {MutationCapability.ReadOnly.IsEnabled}");
    Console.WriteLine($"Environment: {MutationCapability.ReadOnly.Environment}");
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
