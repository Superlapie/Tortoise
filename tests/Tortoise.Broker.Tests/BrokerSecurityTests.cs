using Tortoise.Broker.Handling;
using Tortoise.Broker.Ipc;
using Tortoise.Broker.Validation;
using Tortoise.Contracts.Elevation;
using Tortoise.Contracts.Mutation;
using Tortoise.Core.Installation;
using Tortoise.Core.Mutation;
using Tortoise.Core.Planning;
using Tortoise.Core.Updates;
using Tortoise.Security.Broker;

namespace Tortoise.Broker.Tests;

public sealed class BrokerRequestValidatorTests
{
    private const int TestSessionId = 42;
    private const string TestCapability = "unit-test-capability-token";
    private readonly BrokerRequestValidator _validator = new(TestCapability);

    [Fact]
    public void Validate_rejects_replayed_nonce()
    {
        var replayGuard = new BrokerReplayGuard();
        var request = BrokerRequestFactory.Create(BrokerOperation.Ping, TestSessionId, TestCapability);

        Assert.True(_validator.Validate(request, TestSessionId, replayGuard).IsValid);
        Assert.False(_validator.Validate(request, TestSessionId, replayGuard).IsValid);
    }

    [Fact]
    public void Validate_rejects_session_mismatch()
    {
        var replayGuard = new BrokerReplayGuard();
        var request = BrokerRequestFactory.Create(BrokerOperation.Ping, TestSessionId, TestCapability);

        var result = _validator.Validate(request, expectedSessionId: 99, replayGuard);

        Assert.False(result.IsValid);
        Assert.Equal(BrokerErrorCode.SessionMismatch, result.ErrorCode);
    }

    [Fact]
    public void Validate_rejects_missing_capability_token()
    {
        var replayGuard = new BrokerReplayGuard();
        var request = BrokerRequestFactory.Create(BrokerOperation.Ping, TestSessionId, TestCapability) with
        {
            CapabilityToken = null,
        };

        var result = _validator.Validate(request, TestSessionId, replayGuard);

        Assert.False(result.IsValid);
        Assert.Equal(BrokerErrorCode.UnauthorizedClient, result.ErrorCode);
    }

    [Fact]
    public void Validate_rejects_install_driver_when_not_enabled()
    {
        var replayGuard = new BrokerReplayGuard();
        var request = BrokerRequestFactory.CreateInstallDriver(
            TestSessionId,
            TestCapability,
            Guid.NewGuid(),
            new string('A', 64),
            Guid.NewGuid().ToString(),
            1);

        var result = _validator.Validate(request, TestSessionId, replayGuard, allowDriverInstall: false);

        Assert.False(result.IsValid);
        Assert.Equal(BrokerErrorCode.MutationDisabled, result.ErrorCode);
    }

    [Fact]
    public void Validate_accepts_install_driver_when_enabled_and_payload_valid()
    {
        var replayGuard = new BrokerReplayGuard();
        var request = BrokerRequestFactory.CreateInstallDriver(
            TestSessionId,
            TestCapability,
            Guid.NewGuid(),
            new string('A', 64),
            Guid.NewGuid().ToString(),
            1);

        var result = _validator.Validate(request, TestSessionId, replayGuard, allowDriverInstall: true);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_requires_plan_hash_for_validate_plan()
    {
        var replayGuard = new BrokerReplayGuard();
        var request = BrokerRequestFactory.Create(
            BrokerOperation.ValidatePlan,
            TestSessionId,
            TestCapability,
            planId: Guid.NewGuid(),
            planHash: null);

        var result = _validator.Validate(request, TestSessionId, replayGuard);

        Assert.False(result.IsValid);
        Assert.Equal(BrokerErrorCode.PlanValidationFailed, result.ErrorCode);
    }
}

public sealed class BrokerRequestHandlerTests
{
    private const int TestSessionId = 7;
    private const string TestCapability = "handler-test-capability";

    [Fact]
    public async Task HandleAsync_rejects_non_install_operations_in_install_only_mode()
    {
        var handler = CreateHandler(allowDriverInstall: true);
        var request = BrokerRequestFactory.Create(BrokerOperation.Ping, TestSessionId, TestCapability);
        var response = await handler.HandleAsync(
            request,
            CreateOptions(true, installOnlyMode: true),
            CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal(BrokerErrorCode.OperationNotAllowed, response.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_returns_status_with_install_disabled_by_default()
    {
        var handler = CreateHandler(allowDriverInstall: false);
        var request = BrokerRequestFactory.Create(BrokerOperation.GetStatus, TestSessionId, TestCapability);
        var response = await handler.HandleAsync(request, CreateOptions(false), CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("\"driverInstallEnabled\":false", response.PayloadJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ValidatePlan", response.PayloadJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_reports_install_enabled_in_vm_mode()
    {
        var handler = CreateHandler(
            allowDriverInstall: true,
            environment: new ExecutionEnvironmentInfo(true, true, true));
        var request = BrokerRequestFactory.Create(BrokerOperation.GetStatus, TestSessionId, TestCapability);
        var response = await handler.HandleAsync(request, CreateOptions(true), CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("\"driverInstallEnabled\":true", response.PayloadJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InstallDriver", response.PayloadJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_rejects_install_when_live_verifier_missing()
    {
        var handler = CreateHandler(
            allowDriverInstall: true,
            environment: new ExecutionEnvironmentInfo(true, true, true),
            installService: new FakeWindowsUpdateDriverInstallService(),
            planAuthority: new FakeBrokerPlanAuthority(authorized: true),
            liveInstallVerifier: null,
            useDefaultLiveVerifier: false);

        var request = BrokerRequestFactory.CreateInstallDriver(
            TestSessionId,
            TestCapability,
            Guid.NewGuid(),
            new string('A', 64),
            Guid.NewGuid().ToString(),
            1);

        var response = await handler.HandleAsync(request, CreateOptions(true), CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal(BrokerErrorCode.MutationDisabled, response.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_install_driver_uses_fake_install_service_in_vm_mode()
    {
        var planId = Guid.NewGuid();
        var handler = CreateHandler(
            allowDriverInstall: true,
            environment: new ExecutionEnvironmentInfo(true, true, true),
            installService: new FakeWindowsUpdateDriverInstallService(),
            planAuthority: new FakeBrokerPlanAuthority(authorized: true));

        var request = BrokerRequestFactory.CreateInstallDriver(
            TestSessionId,
            TestCapability,
            planId,
            new string('A', 64),
            Guid.NewGuid().ToString(),
            1);

        var response = await handler.HandleAsync(request, CreateOptions(true), CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.Contains("completed", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_rejects_install_when_plan_authority_fails()
    {
        var handler = CreateHandler(
            allowDriverInstall: true,
            environment: new ExecutionEnvironmentInfo(true, true, true),
            installService: new FakeWindowsUpdateDriverInstallService(),
            planAuthority: new FakeBrokerPlanAuthority(authorized: false));

        var request = BrokerRequestFactory.CreateInstallDriver(
            TestSessionId,
            TestCapability,
            Guid.NewGuid(),
            new string('A', 64),
            Guid.NewGuid().ToString(),
            1);

        var response = await handler.HandleAsync(request, CreateOptions(true), CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal(BrokerErrorCode.PlanValidationFailed, response.ErrorCode);
    }

    private static BrokerHostOptions CreateOptions(bool allowDriverInstall, bool installOnlyMode = false) =>
        new()
        {
            SessionId = TestSessionId,
            CapabilityToken = TestCapability,
            AllowDriverInstall = allowDriverInstall,
            InstallOnlyMode = installOnlyMode,
        };

    private static BrokerRequestHandler CreateHandler(
        bool allowDriverInstall,
        ExecutionEnvironmentInfo? environment = null,
        IWindowsUpdateDriverInstallService? installService = null,
        IBrokerPlanAuthority? planAuthority = null,
        IBrokerLiveInstallVerifier? liveInstallVerifier = null,
        bool useDefaultLiveVerifier = true)
    {
        return new BrokerRequestHandler(
            CreateOptions(allowDriverInstall),
            new StaticExecutionEnvironmentDetector(
                environment ?? new ExecutionEnvironmentInfo(false, false, false)),
            planAuthority ?? new FakeBrokerPlanAuthority(authorized: true),
            installService,
            useDefaultLiveVerifier ? liveInstallVerifier ?? new FakeBrokerLiveInstallVerifier() : liveInstallVerifier);
    }

    private sealed class FakeBrokerLiveInstallVerifier : IBrokerLiveInstallVerifier
    {
        public Task<BrokerPlanAuthorityResult> VerifyInstallBoundaryAsync(
            StoredUpdatePlan storedPlan,
            BrokerInstallPayload payload,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BrokerPlanAuthorityResult(true, "TOCTOU passed.", storedPlan));
    }

    private sealed class FakeWindowsUpdateDriverInstallService : IWindowsUpdateDriverInstallService
    {
        public Task<WindowsUpdateDownloadResult> DownloadAsync(
            string updateId,
            int revision,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WindowsUpdateDownloadResult(true, 2, 0, "Fake download completed."));

        public Task<WindowsUpdateInstallResult> InstallPreparedAsync(
            string updateId,
            int revision,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WindowsUpdateInstallResult(
                true,
                ResultCode: 2,
                UpdateHResult: 0,
                RebootRequired: false,
                "Fake Windows Update install completed."));

        public Task<bool> IsUpdatePreparedAsync(
            string updateId,
            int revision,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<WindowsUpdateInstallResult> InstallAsync(
            string updateId,
            int revision,
            CancellationToken cancellationToken = default) =>
            InstallPreparedAsync(updateId, revision, cancellationToken);
    }

    private sealed class FakeBrokerPlanAuthority : IBrokerPlanAuthority
    {
        private readonly bool _authorized;

        public FakeBrokerPlanAuthority(bool authorized) => _authorized = authorized;

        public Task<BrokerPlanAuthorityResult> ValidateAsync(
            Guid planId,
            string planHash,
            BrokerInstallPayload? installPayload,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BrokerPlanAuthorityResult(
                _authorized,
                _authorized ? "Authorized." : "Plan authority rejected the request.",
                _authorized ? CreateStoredPlan() : null));

        private static StoredUpdatePlan CreateStoredPlan()
        {
            var builder = new UpdatePlanBuilder();
            var device = new Tortoise.Core.Devices.DeviceInventoryEntry(
                new Tortoise.Core.Devices.DeviceSnapshot(
                    new Tortoise.Core.Devices.DeviceIdentity(
                        "ROOT\\NET\\0001",
                        null,
                        Guid.NewGuid(),
                        "Net",
                        "Intel Adapter",
                        "Intel",
                        ["PCI\\VEN_8086&DEV_1234"],
                        [],
                        null,
                        null,
                        null,
                        null,
                        true),
                    new Tortoise.Core.Devices.DeviceHealth(
                        Tortoise.Core.Devices.DeviceHealthState.Healthy,
                        null,
                        null),
                    DateTimeOffset.UtcNow),
                new Tortoise.Core.Devices.DeviceDriverBinding("Intel", new Version(1, 0), null, "intel.inf"));

            var recommendation = new Tortoise.Core.Recommendations.DeviceUpdateRecommendation(
                device,
                new WindowsUpdateCandidate(
                    "update-id",
                    1,
                    "Intel Network Driver",
                    null,
                    "Intel",
                    "Net",
                    "Intel Adapter",
                    new Version(2, 0),
                    null,
                    UpdateClassification.WindowsRecommended,
                    false,
                    false,
                    false,
                    false,
                    [],
                    "PCI\\VEN_8086&DEV_1234"),
                UpdateClassification.WindowsRecommended,
                DriverRiskLevel.Low,
                "Recommended by Windows",
                "Explanation",
                DateTimeOffset.UtcNow);

            var scanResult = new Tortoise.Core.Recommendations.RecommendationScanResult(
                [recommendation],
                [],
                new WindowsUpdatePolicyInfo(false, "Windows Update", "Update source: Windows Update."),
                [],
                DateTimeOffset.UtcNow);

            return builder.BuildPlans(scanResult, new UpdatePlanningOptions()).Single();
        }
    }
}

public sealed class BrokerPlanAuthorityTests
{
    [Fact]
    public async Task ValidateAsync_rejects_mismatched_install_payload()
    {
        var storedPlan = CreateStoredPlan();
        var store = new FakeUpdatePlanStore(storedPlan);
        var authority = new BrokerPlanAuthority(store);

        var result = await authority.ValidateAsync(
            storedPlan.PlanId,
            storedPlan.Plan.PlanHash,
            new BrokerInstallPayload("different-update-id", 99));

        Assert.False(result.IsAuthorized);
    }

    [Fact]
    public async Task ValidateAsync_accepts_matching_install_payload()
    {
        var storedPlan = CreateStoredPlan();
        var store = new FakeUpdatePlanStore(storedPlan);
        var authority = new BrokerPlanAuthority(store);
        var candidate = storedPlan.Plan.ProposedUpdate.Candidate;

        var result = await authority.ValidateAsync(
            storedPlan.PlanId,
            storedPlan.Plan.PlanHash,
            new BrokerInstallPayload(candidate.UpdateIdentity, candidate.UpdateRevision));

        Assert.True(result.IsAuthorized);
    }

    private static StoredUpdatePlan CreateStoredPlan()
    {
        var builder = new UpdatePlanBuilder();
        var device = CreateDevice(new Version(1, 0));
        var recommendation = new Tortoise.Core.Recommendations.DeviceUpdateRecommendation(
            device,
            new WindowsUpdateCandidate(
                "update-id",
                1,
                "Intel Network Driver",
                null,
                "Intel",
                "Net",
                "Intel Adapter",
                new Version(2, 0),
                null,
                UpdateClassification.WindowsRecommended,
                false,
                false,
                false,
                false,
                []),
            UpdateClassification.WindowsRecommended,
            DriverRiskLevel.Low,
            "Recommended by Windows",
            "Explanation",
            DateTimeOffset.UtcNow);

        var scanResult = new Tortoise.Core.Recommendations.RecommendationScanResult(
            [recommendation],
            [],
            new WindowsUpdatePolicyInfo(false, "Windows Update", "Update source: Windows Update."),
            [],
            DateTimeOffset.UtcNow);

        return builder.BuildPlans(scanResult, new UpdatePlanningOptions()).Single();
    }

    private static Tortoise.Core.Devices.DeviceInventoryEntry CreateDevice(Version installedVersion)
    {
        return new Tortoise.Core.Devices.DeviceInventoryEntry(
            new Tortoise.Core.Devices.DeviceSnapshot(
                new Tortoise.Core.Devices.DeviceIdentity(
                    "ROOT\\NET\\0001",
                    null,
                    Guid.NewGuid(),
                    "Net",
                    "Intel Adapter",
                    "Intel",
                    ["PCI\\VEN_8086&DEV_1234"],
                    [],
                    null,
                    null,
                    null,
                    null,
                    true),
                new Tortoise.Core.Devices.DeviceHealth(Tortoise.Core.Devices.DeviceHealthState.Healthy, null, null),
                DateTimeOffset.UtcNow),
            new Tortoise.Core.Devices.DeviceDriverBinding("Intel", installedVersion, null, "intel.inf"));
    }

    private sealed class FakeUpdatePlanStore : IUpdatePlanStore
    {
        private readonly StoredUpdatePlan _plan;

        public FakeUpdatePlanStore(StoredUpdatePlan plan) => _plan = plan;

        public Task SavePlansAsync(IReadOnlyList<StoredUpdatePlan> plans, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<StoredUpdatePlan?> GetPlanAsync(Guid planId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_plan.PlanId == planId ? _plan : null);

        public Task<IReadOnlyList<StoredUpdatePlan>> ListPlansAsync(
            long? scanSessionId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredUpdatePlan>>([_plan]);
    }
}

public sealed class BrokerPipeIntegrationTests
{
    [Fact]
    public async Task Client_and_server_exchange_ping_over_named_pipe()
    {
        var sessionId = Random.Shared.Next(1000, 9999);
        var capability = Guid.NewGuid().ToString("N");
        var pipeName = ElevationConstants.GetPipeName(sessionId, capability);
        var options = new BrokerHostOptions
        {
            SessionId = sessionId,
            CapabilityToken = capability,
            PipeName = pipeName,
            ConnectTimeoutMs = 5000,
        };

        var serverTask = BrokerHost.RunOnceAsync(options);
        await Task.Delay(100);

        var client = new BrokerPipeClient();
        var response = await client.SendAsync(
            options,
            BrokerRequestFactory.Create(BrokerOperation.Ping, sessionId, capability));

        Assert.True(response.Succeeded);
        await serverTask;
    }
}
