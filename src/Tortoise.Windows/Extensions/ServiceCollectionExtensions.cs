using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.FaultInjection;
using Tortoise.Core.Installation;
using Tortoise.Core.Mutation;
using Tortoise.Core.Pilot;
using Tortoise.Core.Planning;
using Tortoise.Core.Recovery;
using Tortoise.Core.Recommendations;
using Tortoise.Core.Updates;
using Tortoise.Windows.Devices;
using Tortoise.Windows.Drivers;
using Tortoise.Windows.Recovery;
using Tortoise.WindowsUpdate.Environment;
using Tortoise.WindowsUpdate.Extensions;

namespace Tortoise.Windows.Extensions;

public static class ServiceCollectionExtensions
{
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddTortoiseWindowsDeviceInventory(this IServiceCollection services)
    {
        services.AddSingleton<IDeviceInventoryProvider, WindowsDeviceInventoryProvider>();
        return services;
    }

    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddTortoiseWindowsDriverPackageInventory(this IServiceCollection services)
    {
        services.AddTortoiseWindowsDeviceInventory();
        services.AddSingleton<IDriverPackageInventoryProvider, WindowsDriverPackageInventoryProvider>();
        services.AddSingleton<IDriverPackageExportService, DisabledDriverPackageExportService>();
        return services;
    }

    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddTortoiseRecommendations(this IServiceCollection services)
    {
        services.AddTortoiseWindowsDeviceInventory();
        services.AddTortoiseWindowsUpdate();
        services.AddSingleton<IRecommendationEngine, RecommendationEngine>();
        services.AddSingleton<IRecommendationScanService, RecommendationScanService>();
        return services;
    }

    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddTortoiseUpdatePlanning(this IServiceCollection services)
    {
        services.AddTortoiseRecommendations();
        services.AddSingleton<IUpdatePlanBuilder, UpdatePlanBuilder>();
        services.AddSingleton<IUpdatePreflightService, UpdatePreflightService>();
        services.AddSingleton<IUpdatePlanService, UpdatePlanService>();
        services.AddSingleton<ISimulatedUpdatePackageVerificationService, SimulatedUpdatePackageVerificationService>();
        services.AddSingleton<ISimulatedPostInstallVerificationService, SimulatedPostInstallVerificationService>();
        services.AddSingleton<ISimulatedUpdateTransactionService, SimulatedUpdateTransactionService>();
        return services;
    }

    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddTortoiseSimulatedMutationWorkflow(this IServiceCollection services)
    {
        services.AddTortoiseUpdatePlanning();
        services.AddSingleton<ISimulatedMutationWorkflowService, SimulatedMutationWorkflowService>();
        return services;
    }

    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddTortoiseVmDriverInstall(this IServiceCollection services)
    {
        services.AddTortoiseUpdatePlanning();
        services.AddSingleton<IExecutionEnvironmentDetector, WindowsExecutionEnvironmentDetector>();
        services.AddSingleton<IRealPostInstallVerificationService, RealPostInstallVerificationService>();
        services.AddSingleton<IVmDriverInstallService, VmDriverInstallService>();
        return services;
    }

    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddTortoiseFaultInjection(this IServiceCollection services)
    {
        services.AddTortoiseSimulatedMutationWorkflow();
        services.AddSingleton<IFaultInjectionWorkflowService, FaultInjectionWorkflowService>();
        services.AddSingleton<IFaultReconciliationService, FaultReconciliationService>();
        return services;
    }

    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddTortoiseRecoveryPreparation(this IServiceCollection services)
    {
        services.AddTortoiseUpdatePlanning();
        services.AddTortoiseWindowsDriverPackageInventory();
        services.AddSingleton<ISystemEnvironmentProvider, WindowsSystemEnvironmentProvider>();
        services.AddSingleton<ISystemRestoreInfoProvider, WindowsSystemRestoreInfoProvider>();
        services.AddSingleton<IRecoveryPreparationService, RecoveryPreparationService>();
        return services;
    }

    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddTortoisePhysicalPilotReadiness(this IServiceCollection services)
    {
        services.AddTortoiseRecoveryPreparation();
        services.AddSingleton<IExecutionEnvironmentDetector, WindowsExecutionEnvironmentDetector>();
        services.AddSingleton<IPhysicalPilotChecklistService, PhysicalPilotChecklistService>();
        services.AddSingleton<IPhysicalPilotConfirmationService, PhysicalPilotConfirmationService>();
        services.AddSingleton<IPhysicalPilotRecoveryGuide, PhysicalPilotRecoveryGuide>();
        return services;
    }
}
