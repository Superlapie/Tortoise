using Microsoft.Extensions.DependencyInjection;
using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Diagnostics;
using Tortoise.Core.Drivers;
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
