using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Planning;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Recovery;

public sealed class RecoveryPreparationService : IRecoveryPreparationService
{
    private readonly IUpdatePlanService _planService;
    private readonly IDeviceInventoryProvider _deviceInventoryProvider;
    private readonly IDriverPackageInventoryProvider _packageInventoryProvider;
    private readonly IDriverPackageExportService _exportService;
    private readonly ISystemEnvironmentProvider _systemEnvironmentProvider;
    private readonly ISystemRestoreInfoProvider _systemRestoreInfoProvider;
    private readonly IRecoveryPreparationStore _store;

    public RecoveryPreparationService(
        IUpdatePlanService planService,
        IDeviceInventoryProvider deviceInventoryProvider,
        IDriverPackageInventoryProvider packageInventoryProvider,
        IDriverPackageExportService exportService,
        ISystemEnvironmentProvider systemEnvironmentProvider,
        ISystemRestoreInfoProvider systemRestoreInfoProvider,
        IRecoveryPreparationStore store)
    {
        _planService = planService;
        _deviceInventoryProvider = deviceInventoryProvider;
        _packageInventoryProvider = packageInventoryProvider;
        _exportService = exportService;
        _systemEnvironmentProvider = systemEnvironmentProvider;
        _systemRestoreInfoProvider = systemRestoreInfoProvider;
        _store = store;
    }

    public async Task<RecoveryPreparationRecord> PrepareAsync(
        Guid planId,
        string? exportDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var storedPlan = await _planService.GetPlanAsync(planId, cancellationToken)
            ?? throw new InvalidOperationException($"Plan '{planId}' was not found.");

        var inventory = await _deviceInventoryProvider.ScanAsync(cancellationToken);
        var currentDevice = inventory.Devices.SingleOrDefault(device =>
            string.Equals(
                device.Snapshot.Identity.DeviceInstanceId,
                storedPlan.Plan.DeviceSnapshot.Identity.DeviceInstanceId,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Target device is no longer present in the current inventory.");

        var beforeDriver = RecommendationPlanMapper.ToInstalledDriver(currentDevice);
        var preparationId = Guid.NewGuid();
        var preparedAt = DateTimeOffset.UtcNow;

        var exportAttempt = await AttemptPackageExportAsync(
            storedPlan,
            currentDevice,
            preparationId,
            exportDirectory,
            cancellationToken);

        var snapshot = new RecoverySnapshot(
            preparationId,
            currentDevice.Snapshot,
            beforeDriver,
            exportAttempt.ExportedPackagePath,
            preparedAt);

        var systemSnapshot = await _systemEnvironmentProvider.CaptureAsync(cancellationToken);
        var systemRestore = await _systemRestoreInfoProvider.GetInfoAsync(cancellationToken);

        var record = new RecoveryPreparationRecord(
            preparationId,
            planId,
            TransactionId: null,
            preparedAt,
            snapshot,
            systemSnapshot,
            exportAttempt,
            systemRestore,
            storedPlan.Plan);

        await _store.SaveAsync(record, cancellationToken);
        return record;
    }

    public Task<RecoveryPreparationRecord?> GetAsync(
        Guid preparationId,
        CancellationToken cancellationToken = default) =>
        _store.GetAsync(preparationId, cancellationToken);

    public Task<IReadOnlyList<RecoveryPreparationSummary>> ListAsync(
        Guid? planId = null,
        CancellationToken cancellationToken = default) =>
        _store.ListAsync(planId, cancellationToken);

    private async Task<RecoveryExportAttempt> AttemptPackageExportAsync(
        StoredUpdatePlan storedPlan,
        DeviceInventoryEntry currentDevice,
        Guid preparationId,
        string? exportDirectory,
        CancellationToken cancellationToken)
    {
        if (!_exportService.IsExportEnabled)
        {
            return new RecoveryExportAttempt(
                ExportServiceEnabled: false,
                Succeeded: false,
                ExportedPackagePath: null,
                Message: _exportService.DisabledReason,
                MatchedPackageName: null);
        }

        var packageInventory = await _packageInventoryProvider.ScanAsync([currentDevice], cancellationToken);
        var association = packageInventory.Associations.FirstOrDefault(entry =>
            string.Equals(
                entry.DeviceInstanceId,
                currentDevice.Snapshot.Identity.DeviceInstanceId,
                StringComparison.OrdinalIgnoreCase)
            && entry.AssociationKind == DriverAssociationKind.Active);

        if (association is null)
        {
            return new RecoveryExportAttempt(
                ExportServiceEnabled: true,
                Succeeded: false,
                ExportedPackagePath: null,
                Message: "No active driver store package association was found for the target device.",
                MatchedPackageName: null);
        }

        var destinationDirectory = exportDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Tortoise",
                "Recovery",
                preparationId.ToString("N"));

        var exportResult = await _exportService.ExportAsync(
            association.Package,
            destinationDirectory,
            cancellationToken);

        return new RecoveryExportAttempt(
            ExportServiceEnabled: true,
            Succeeded: exportResult.Succeeded,
            ExportedPackagePath: exportResult.ExportedPath,
            Message: exportResult.Message,
            MatchedPackageName: association.Package.PublishedInfName);
    }
}
