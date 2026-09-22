using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Policy;
using Tortoise.Core.Transactions;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Tests.Transactions;

public sealed class UpdateTransactionTransitionTests
{
    [Theory]
    [InlineData(UpdateTransactionState.Discovered, UpdateTransactionState.Installing, false)]
    [InlineData(UpdateTransactionState.Discovered, UpdateTransactionState.Eligible, true)]
    [InlineData(UpdateTransactionState.PreflightPassed, UpdateTransactionState.Downloading, true)]
    [InlineData(UpdateTransactionState.InstallReturned, UpdateTransactionState.PostInstallChecking, true)]
    [InlineData(UpdateTransactionState.PreflightPassed, UpdateTransactionState.Failed, true)]
    public void CanTransition_enforces_state_machine(
        UpdateTransactionState from,
        UpdateTransactionState to,
        bool expected)
    {
        Assert.Equal(expected, UpdateTransactionTransitions.CanTransition(from, to));
    }
}

public sealed class StalePlanDetectorTests
{
    [Fact]
    public void IsStale_returns_true_when_driver_version_changed()
    {
        var plan = CreatePlan(driverVersion: new Version(1, 0));
        var currentDriver = CreateInstalledDriver(new Version(2, 0));

        Assert.True(StalePlanDetector.IsStale(
            plan,
            plan.DeviceSnapshot,
            currentDriver,
            UpdateClassification.WindowsRecommended,
            DriverRiskLevel.Low));
    }

    [Fact]
    public void IsStale_returns_false_when_nothing_material_changed()
    {
        var plan = CreatePlan(driverVersion: new Version(1, 0));
        var currentDriver = CreateInstalledDriver(new Version(1, 0));

        Assert.False(StalePlanDetector.IsStale(
            plan,
            plan.DeviceSnapshot,
            currentDriver,
            UpdateClassification.WindowsRecommended,
            DriverRiskLevel.Low));
    }

    private static UpdatePlan CreatePlan(Version driverVersion)
    {
        var identity = CreateDeviceIdentity();
        var snapshot = new DeviceSnapshot(
            identity,
            new DeviceHealth(DeviceHealthState.Healthy, null, null),
            DateTimeOffset.UtcNow);
        var currentDriver = CreateInstalledDriver(driverVersion);
        var update = CreateUpdate();
        return UpdatePlanFactory.Create(
            snapshot,
            currentDriver,
            update,
            "1",
            "0.1.0-alpha",
            false,
            UpdateClassification.WindowsRecommended,
            DriverRiskLevel.Low);
    }

    private static InstalledDriver CreateInstalledDriver(Version version)
    {
        return new InstalledDriver(CreatePackage(version), "ROOT\\TEST\\0000", null);
    }

    private static DriverUpdate CreateUpdate()
    {
        var package = CreatePackage(new Version(2, 0));
        var candidate = new DriverCandidate(package, "ROOT\\TEST\\0000", "update-id", 1);
        return new DriverUpdate(
            candidate,
            UpdateClassification.WindowsRecommended,
            DriverRiskLevel.Low,
            false,
            false,
            "Windows Update reports this package as applicable.");
    }

    private static DriverPackage CreatePackage(Version version)
    {
        var identity = new DriverIdentity(
            "test.inf",
            "oemtest.inf",
            "Contoso",
            "Bluetooth",
            Guid.NewGuid(),
            version,
            new DateOnly(2024, 1, 1),
            "Contoso",
            "test.cat",
            "pkg-1",
            "ROOT\\TEST");
        var signature = new DriverSignature(true, true, "Contoso", "test.cat");
        var source = new DriverSource(DriverSourceKind.WindowsUpdate, "windows-update", "Windows Update", true);
        return new DriverPackage(identity, signature, source);
    }

    private static DeviceIdentity CreateDeviceIdentity()
    {
        return new DeviceIdentity(
            "ROOT\\TEST\\0000",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Bluetooth",
            "Test Bluetooth Adapter",
            "Contoso",
            ["ROOT\\TEST"],
            [],
            "USB",
            "ROOT",
            null,
            null,
            true);
    }
}

public sealed class DefaultRiskPolicyTests
{
    [Fact]
    public void Classify_marks_firmware_as_restricted()
    {
        var policy = new DefaultRiskPolicy();
        var candidate = CreateCandidate();
        var context = new DeviceContext("ROOT\\TEST\\0000", "Firmware", false, false, false, true);

        Assert.Equal(DriverRiskLevel.Restricted, policy.Classify(candidate, context));
    }

    [Fact]
    public void Classify_marks_active_wifi_as_moderate()
    {
        var policy = new DefaultRiskPolicy();
        var candidate = CreateCandidate();
        var context = new DeviceContext("ROOT\\TEST\\0000", "Net", false, true, false, false);

        Assert.Equal(DriverRiskLevel.Moderate, policy.Classify(candidate, context));
    }

    private static DriverCandidate CreateCandidate()
    {
        var package = new DriverPackage(
            new DriverIdentity("a.inf", "a.inf", "Intel", "Net", Guid.NewGuid(), new Version(1, 0), null, null, null, null, null),
            new DriverSignature(true, true, "Intel", "a.cat"),
            new DriverSource(DriverSourceKind.WindowsUpdate, "windows-update", "Windows Update", true));
        return new DriverCandidate(package, "ROOT\\TEST\\0000", "id", 1);
    }
}

public sealed class DefaultUpdateSourcePolicyTests
{
    [Fact]
    public void IsSourceAllowed_rejects_untrusted_sources()
    {
        var policy = new DefaultUpdateSourcePolicy();
        var source = new DriverSource(DriverSourceKind.Unknown, "mirror", "Random Mirror", false);

        Assert.False(policy.IsSourceAllowed(source));
    }

    [Fact]
    public void ClassifyUpdate_marks_firmware_as_restricted()
    {
        var policy = new DefaultUpdateSourcePolicy();
        var update = new DriverUpdate(
            new DriverCandidate(
                new DriverPackage(
                    new DriverIdentity("fw.inf", "fw.inf", "OEM", "Firmware", Guid.NewGuid(), new Version(1, 0), null, null, null, null, null),
                    new DriverSignature(true, true, "OEM", "fw.cat"),
                    new DriverSource(DriverSourceKind.WindowsUpdate, "windows-update", "Windows Update", true)),
                "ROOT\\TEST\\0000",
                "fw",
                1),
            UpdateClassification.WindowsRecommended,
            DriverRiskLevel.High,
            true,
            false,
            "Firmware update");

        var context = new DeviceContext("ROOT\\TEST\\0000", "Firmware", true, false, false, true);

        Assert.Equal(UpdateClassification.Restricted, policy.ClassifyUpdate(update, context));
    }
}
