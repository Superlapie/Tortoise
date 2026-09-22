using Tortoise.Core.Devices;
using Tortoise.Core.Drivers;

namespace Tortoise.Core.Updates;

public enum UpdateClassification
{
    Current = 0,
    WindowsRecommended = 1,
    WindowsOptional = 2,
    VendorNewer = 3,
    ReviewRequired = 4,
    Restricted = 5,
    Unknown = 6,
}

public enum DriverRiskLevel
{
    Low = 0,
    Moderate = 1,
    High = 2,
    Restricted = 3,
}

public sealed record UpdateRecommendation(
    string DeviceInstanceId,
    UpdateClassification Classification,
    DriverRiskLevel RiskLevel,
    string Summary,
    string? Detail,
    DateTimeOffset EvaluatedAtUtc);

public sealed record UpdatePlan(
    Guid PlanId,
    DateTimeOffset CreatedAtUtc,
    DeviceSnapshot DeviceSnapshot,
    InstalledDriver CurrentDriver,
    DriverUpdate ProposedUpdate,
    string SafetyPolicyVersion,
    string ApplicationVersion,
    bool RestartExpected,
    string PlanHash);

public sealed record UpdatePreflightCheck(
    string Name,
    PreflightResult Result,
    string Message);

public enum PreflightResult
{
    Passed = 0,
    Warning = 1,
    Blocked = 2,
    Unknown = 3,
}

public sealed record UpdatePreflight(
    Guid PlanId,
    IReadOnlyList<UpdatePreflightCheck> Checks,
    bool IsBlocked);

public enum UpdateVerificationResult
{
    Verified = 0,
    InstalledRestartRequired = 1,
    InstalledInconclusive = 2,
    Failed = 3,
    RecoveryRequired = 4,
}

public sealed record UpdateVerification(
    Guid TransactionId,
    UpdateVerificationResult Result,
    string Message,
    DateTimeOffset VerifiedAtUtc);

public sealed record RecoverySnapshot(
    Guid TransactionId,
    DeviceSnapshot BeforeDevice,
    InstalledDriver BeforeDriver,
    string? ExportedPackagePath,
    DateTimeOffset CapturedAtUtc);

public sealed record SystemSnapshot(
    string WindowsVersion,
    string Architecture,
    bool PendingReboot,
    DateTimeOffset CapturedAtUtc);
