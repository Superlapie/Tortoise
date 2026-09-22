using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;
using Tortoise.Core.Recommendations;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Tests.Recommendations;

public sealed class RecommendationEngineTests
{
    private readonly RecommendationEngine _engine = new();

    [Fact]
    public void Evaluate_marks_device_without_match_as_current_when_scan_is_empty()
    {
        var devices = new[] { CreateDevice("ROOT\\NET\\0001", "Net", "Intel", ["PCI\\VEN_8086"]) };
        var updateScan = CreateUpdateScan([]);

        var result = _engine.Evaluate(devices, updateScan);

        var recommendation = Assert.Single(result.Recommendations);
        Assert.Equal(UpdateClassification.Current, recommendation.Classification);
        Assert.Equal(RecommendationTerminology.CurrentSummary, recommendation.Summary);
        Assert.DoesNotContain("latest", recommendation.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_prefers_hardware_id_match_over_generic_class_match()
    {
        var devices = new[]
        {
            CreateDevice("ROOT\\NET\\0001", "Net", "Intel", ["PCI\\VEN_8086&DEV_1234"]),
            CreateDevice("ROOT\\NET\\0002", "Net", "Intel", ["PCI\\VEN_10EC&DEV_9999"]),
        };

        var genericUpdate = CreateUpdate(
            "generic-id",
            "Intel Net Driver",
            "Intel",
            "Net",
            driverHardwareId: "PCI\\VEN_10EC&DEV_9999");
        var exactUpdate = CreateUpdate(
            "exact-id",
            "Intel Driver",
            "Intel",
            "Net",
            driverHardwareId: "PCI\\VEN_8086&DEV_1234");

        var updateScan = CreateUpdateScan([genericUpdate, exactUpdate]);
        var result = _engine.Evaluate(devices, updateScan);

        var firstDevice = result.Recommendations.Single(recommendation =>
            recommendation.Device.Snapshot.Identity.DeviceInstanceId == "ROOT\\NET\\0001");
        var secondDevice = result.Recommendations.Single(recommendation =>
            recommendation.Device.Snapshot.Identity.DeviceInstanceId == "ROOT\\NET\\0002");

        Assert.Equal("exact-id", firstDevice.ApplicableUpdate?.UpdateId);
        Assert.Equal("generic-id", secondDevice.ApplicableUpdate?.UpdateId);
    }

    [Fact]
    public void Evaluate_marks_device_unknown_when_unmapped_driver_updates_exist()
    {
        var device = CreateDevice(
            "ROOT\\NET\\0001",
            "Net",
            "Intel",
            ["PCI\\VEN_8086"],
            installedVersion: new Version(99, 0));

        var update = CreateUpdate(
            "other-device-id",
            "Other Vendor Driver",
            "Contoso",
            "Net",
            driverHardwareId: "PCI\\VEN_10EC&DEV_9999");

        var result = _engine.Evaluate([device], CreateUpdateScan([update]));

        Assert.Equal(UpdateClassification.Unknown, Assert.Single(result.Recommendations).Classification);
    }

    [Fact]
    public void Evaluate_marks_firmware_related_update_as_restricted()
    {
        var device = CreateDevice("ROOT\\FIRMWARE\\0001", "Firmware", "OEM", ["ROOT\\FIRMWARE"]);
        var update = CreateUpdate(
            "fw-id",
            "UEFI firmware update",
            "OEM",
            "Firmware",
            categories: ["Firmware"]);

        var result = _engine.Evaluate([device], CreateUpdateScan([update]));
        var recommendation = Assert.Single(result.Recommendations);

        Assert.Equal(UpdateClassification.Restricted, recommendation.Classification);
        Assert.Equal(DriverRiskLevel.Restricted, recommendation.RiskLevel);
    }

    [Fact]
    public void Evaluate_excludes_optional_updates_by_default()
    {
        var device = CreateDevice("ROOT\\NET\\0001", "Net", "Intel", ["PCI\\VEN_8086&DEV_1234"]);
        var optionalUpdate = CreateUpdate(
            "optional-id",
            "Intel Driver",
            "Intel",
            "Net",
            classification: UpdateClassification.WindowsOptional,
            driverHardwareId: "PCI\\VEN_8086&DEV_1234");

        var result = _engine.Evaluate([device], CreateUpdateScan([optionalUpdate]));

        Assert.Equal(UpdateClassification.Current, Assert.Single(result.Recommendations).Classification);
    }

    [Fact]
    public void Evaluate_includes_optional_updates_when_requested()
    {
        var device = CreateDevice("ROOT\\NET\\0001", "Net", "Intel", ["PCI\\VEN_8086&DEV_1234"]);
        var optionalUpdate = CreateUpdate(
            "optional-id",
            "Intel Driver",
            "Intel",
            "Net",
            classification: UpdateClassification.WindowsOptional,
            driverHardwareId: "PCI\\VEN_8086&DEV_1234");

        var result = _engine.Evaluate(
            [device],
            CreateUpdateScan([optionalUpdate]),
            new RecommendationOptions(IncludeOptionalUpdates: true));

        Assert.Equal(UpdateClassification.WindowsOptional, Assert.Single(result.Recommendations).Classification);
    }

    private static DeviceInventoryEntry CreateDevice(
        string instanceId,
        string className,
        string manufacturer,
        IReadOnlyList<string> hardwareIds,
        Version? installedVersion = null)
    {
        var identity = new DeviceIdentity(
            instanceId,
            null,
            Guid.NewGuid(),
            className,
            $"{manufacturer} Adapter",
            manufacturer,
            hardwareIds,
            [],
            null,
            null,
            null,
            null,
            true);

        var snapshot = new DeviceSnapshot(
            identity,
            new DeviceHealth(DeviceHealthState.Healthy, null, null),
            DateTimeOffset.UtcNow);

        DeviceDriverBinding? binding = installedVersion is null
            ? null
            : new DeviceDriverBinding(manufacturer, installedVersion, null, "device.inf");

        return new DeviceInventoryEntry(snapshot, binding);
    }

    private static WindowsUpdateCandidate CreateUpdate(
        string updateId,
        string title,
        string manufacturer,
        string driverClass,
        Version? version = null,
        string? driverModel = null,
        string? driverHardwareId = null,
        UpdateClassification classification = UpdateClassification.WindowsRecommended,
        IReadOnlyList<string>? categories = null) =>
        new(
            updateId,
            1,
            title,
            null,
            manufacturer,
            driverClass,
            driverModel,
            version,
            null,
            classification,
            false,
            false,
            false,
            false,
            categories ?? [],
            driverHardwareId);

    private static DriverUpdateScanResult CreateUpdateScan(IReadOnlyList<WindowsUpdateCandidate> candidates) =>
        new(
            candidates,
            new WindowsUpdatePolicyInfo(false, "Windows Update", "Update source: Windows Update."),
            [],
            DateTimeOffset.UtcNow);
}

public sealed class UpdateApplicabilityMatcherTests
{
    [Fact]
    public void Score_is_higher_for_hardware_id_match_than_class_only()
    {
        var device = new DeviceInventoryEntry(
            new DeviceSnapshot(
                new DeviceIdentity(
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
                new DeviceHealth(DeviceHealthState.Healthy, null, null),
                DateTimeOffset.UtcNow),
            new DeviceDriverBinding("Intel", new Version(1, 0), null, "intel.inf"));

        var classOnly = new WindowsUpdateCandidate(
            "1",
            1,
            "Intel Network",
            null,
            "Intel",
            "Net",
            null,
            new Version(2, 0),
            null,
            UpdateClassification.WindowsRecommended,
            false,
            false,
            false,
            false,
            []);

        var hardwareMatch = classOnly with
        {
            UpdateId = "2",
            DriverHardwareId = "PCI\\VEN_8086&DEV_1234",
        };

        var classScore = UpdateApplicabilityMatcher.Score(device, classOnly);
        var hardwareScore = UpdateApplicabilityMatcher.Score(device, hardwareMatch);

        Assert.True(hardwareScore > classScore);
        Assert.True(UpdateApplicabilityMatcher.HasStrongMatch(device, hardwareMatch, hardwareScore));
        Assert.False(UpdateApplicabilityMatcher.HasStrongMatch(device, classOnly, classScore));
    }
}

public sealed class RecommendationTerminologyTests
{
    [Fact]
    public void DescribeClassification_never_uses_latest_wording()
    {
        foreach (UpdateClassification classification in Enum.GetValues<UpdateClassification>())
        {
            var summary = RecommendationTerminology.DescribeClassification(classification);
            Assert.DoesNotContain("latest", summary, StringComparison.OrdinalIgnoreCase);
        }
    }
}
