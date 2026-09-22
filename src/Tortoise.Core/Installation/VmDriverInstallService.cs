using Tortoise.Contracts.Mutation;
using Tortoise.Core.Devices;
using Tortoise.Core.Mutation;
using Tortoise.Core.Planning;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Installation;

public sealed class VmDriverInstallService : IVmDriverInstallService
{
    private readonly IExecutionEnvironmentDetector _environmentDetector;
    private readonly IUpdatePlanService _planService;
    private readonly IUpdatePreflightService _preflightService;
    private readonly IDeviceInventoryProvider _deviceInventoryProvider;
    private readonly IBrokerPlanValidationClient? _brokerPlanValidationClient;
    private readonly IBrokerDriverInstallClient? _brokerDriverInstallClient;
    private readonly IRealPostInstallVerificationService _postInstallVerificationService;

    public VmDriverInstallService(
        IExecutionEnvironmentDetector environmentDetector,
        IUpdatePlanService planService,
        IUpdatePreflightService preflightService,
        IDeviceInventoryProvider deviceInventoryProvider,
        IRealPostInstallVerificationService postInstallVerificationService,
        IBrokerPlanValidationClient? brokerPlanValidationClient = null,
        IBrokerDriverInstallClient? brokerDriverInstallClient = null)
    {
        _environmentDetector = environmentDetector;
        _planService = planService;
        _preflightService = preflightService;
        _deviceInventoryProvider = deviceInventoryProvider;
        _postInstallVerificationService = postInstallVerificationService;
        _brokerPlanValidationClient = brokerPlanValidationClient;
        _brokerDriverInstallClient = brokerDriverInstallClient;
    }

    public async Task<VmDriverInstallResult> InstallAsync(
        Guid planId,
        VmDriverInstallOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var capability = MutationCapabilityResolver.Resolve(_environmentDetector.Detect());
        if (!capability.IsEnabled || capability.Environment != MutationEnvironment.DisposableVm)
        {
            throw new MutationDeniedException(
                $"VM-gated driver installation was denied: {capability.Reason}");
        }

        using var servicingLock = MutationServicingLock.TryAcquire();
        if (!servicingLock.IsAcquired)
        {
            throw new MutationDeniedException(
                "VM-gated driver installation was denied because another Tortoise servicing operation is in progress.");
        }

        var storedPlan = await _planService.GetPlanAsync(planId, cancellationToken)
            ?? throw new InvalidOperationException($"Plan '{planId}' was not found.");

        if (storedPlan.RiskLevel != DriverRiskLevel.Low
            || storedPlan.Classification != UpdateClassification.WindowsRecommended)
        {
            return Blocked(
                storedPlan.PlanId,
                null,
                null,
                null,
                $"Plan '{storedPlan.PlanId}' is not eligible for disposable VM install. Only low-risk Windows-recommended updates are allowed.");
        }

        var inventoryBefore = await _deviceInventoryProvider.ScanAsync(cancellationToken);
        var currentDevice = inventoryBefore.Devices.SingleOrDefault(device =>
                string.Equals(
                    device.Snapshot.Identity.DeviceInstanceId,
                    storedPlan.Plan.DeviceSnapshot.Identity.DeviceInstanceId,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Target device is no longer present in the current inventory.");

        var preflight = _preflightService.RunPreflight(
            storedPlan,
            currentDevice,
            capability);

        if (preflight.IsBlocked)
        {
            return Blocked(
                storedPlan.PlanId,
                preflight,
                null,
                null,
                "Preflight blocked the disposable VM install.");
        }

        BrokerPlanValidationResult? brokerValidation = null;
        if (options.RequireBrokerValidation)
        {
            if (_brokerPlanValidationClient is null || options.BrokerOptions is null)
            {
                throw new InvalidOperationException(
                    "Broker plan validation was requested but broker options or client are unavailable.");
            }

            brokerValidation = await _brokerPlanValidationClient.ValidatePlanAsync(
                storedPlan.PlanId,
                storedPlan.Plan.PlanHash,
                options.BrokerOptions,
                cancellationToken);

            if (!brokerValidation.Succeeded)
            {
                return Blocked(
                    storedPlan.PlanId,
                    preflight,
                    brokerValidation,
                    null,
                    $"Broker plan validation failed: {brokerValidation.Message}");
            }
        }

        if (_brokerDriverInstallClient is null || options.BrokerOptions is null)
        {
            throw new InvalidOperationException(
                "Broker driver install client or broker options are unavailable.");
        }

        var candidate = storedPlan.Plan.ProposedUpdate.Candidate;
        var brokerInstall = await _brokerDriverInstallClient.InstallDriverAsync(
            storedPlan.PlanId,
            storedPlan.Plan.PlanHash,
            candidate.UpdateIdentity,
            candidate.UpdateRevision,
            options.BrokerOptions,
            cancellationToken);

        if (!brokerInstall.Succeeded)
        {
            return Blocked(
                storedPlan.PlanId,
                preflight,
                brokerValidation,
                brokerInstall,
                $"Broker driver install failed: {brokerInstall.Message}");
        }

        var inventoryAfter = await _deviceInventoryProvider.ScanAsync(cancellationToken);
        var deviceAfter = inventoryAfter.Devices.SingleOrDefault(device =>
                string.Equals(
                    device.Snapshot.Identity.DeviceInstanceId,
                    storedPlan.Plan.DeviceSnapshot.Identity.DeviceInstanceId,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Target device disappeared after install.");

        var operationId = Guid.NewGuid();
        var postInstallVerification = _postInstallVerificationService.VerifyPostInstall(
            storedPlan,
            currentDevice,
            deviceAfter,
            operationId);

        if (postInstallVerification.Result == UpdateVerificationResult.Failed)
        {
            return new VmDriverInstallResult(
                storedPlan.PlanId,
                preflight,
                brokerValidation,
                brokerInstall,
                postInstallVerification,
                CompletedSuccessfully: false,
                Summary: postInstallVerification.Message);
        }

        var summary = brokerInstall.RebootRequired
            ? "Disposable VM install completed; a reboot may be required."
            : "Disposable VM install completed and post-install verification passed.";

        return new VmDriverInstallResult(
            storedPlan.PlanId,
            preflight,
            brokerValidation,
            brokerInstall,
            postInstallVerification,
            CompletedSuccessfully: true,
            Summary: summary);
    }

    private static VmDriverInstallResult Blocked(
        Guid planId,
        UpdatePreflight? preflight,
        BrokerPlanValidationResult? brokerValidation,
        BrokerDriverInstallResult? brokerInstall,
        string summary) =>
        new(
            planId,
            preflight ?? new UpdatePreflight(planId, [], true),
            brokerValidation,
            brokerInstall,
            new UpdateVerification(
                Guid.Empty,
                UpdateVerificationResult.Failed,
                summary,
                DateTimeOffset.UtcNow),
            CompletedSuccessfully: false,
            Summary: summary);
}
