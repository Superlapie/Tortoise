using Tortoise.Core.Devices;
using Tortoise.Core.Planning;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Installation;

public sealed record VmDriverInstallOptions(
    bool RequireBrokerValidation = false,
    BrokerPlanValidationOptions? BrokerOptions = null);

public sealed record BrokerDriverInstallResult(
    bool Succeeded,
    string Message,
    int ResultCode,
    bool RebootRequired,
    string? ErrorCode = null);

public interface IBrokerDriverInstallClient
{
    Task<BrokerDriverInstallResult> InstallDriverAsync(
        Guid planId,
        string planHash,
        string updateId,
        int revision,
        BrokerPlanValidationOptions options,
        CancellationToken cancellationToken = default);
}

public sealed record VmDriverInstallResult(
    Guid PlanId,
    UpdatePreflight Preflight,
    BrokerPlanValidationResult? BrokerValidation,
    BrokerDriverInstallResult? BrokerInstall,
    UpdateVerification PostInstallVerification,
    bool CompletedSuccessfully,
    string Summary);

public interface IVmDriverInstallService
{
    Task<VmDriverInstallResult> InstallAsync(
        Guid planId,
        VmDriverInstallOptions options,
        CancellationToken cancellationToken = default);
}

public interface IWindowsUpdateDriverInstallService
{
    Task<WindowsUpdateDownloadResult> DownloadAsync(
        string updateId,
        int revision,
        CancellationToken cancellationToken = default);

    Task<WindowsUpdateInstallResult> InstallPreparedAsync(
        string updateId,
        int revision,
        CancellationToken cancellationToken = default);

    Task<WindowsUpdateInstallResult> InstallAsync(
        string updateId,
        int revision,
        CancellationToken cancellationToken = default);
}

public sealed record WindowsUpdateDownloadResult(
    bool Succeeded,
    int ResultCode,
    int UpdateHResult,
    string Message);

public sealed record WindowsUpdateInstallResult(
    bool Succeeded,
    int ResultCode,
    int UpdateHResult,
    bool RebootRequired,
    string Message);

public interface IRealPostInstallVerificationService
{
    UpdateVerification VerifyPostInstall(
        StoredUpdatePlan storedPlan,
        DeviceInventoryEntry deviceBefore,
        DeviceInventoryEntry deviceAfter,
        Guid operationId);
}
