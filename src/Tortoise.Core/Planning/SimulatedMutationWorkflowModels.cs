using Tortoise.Core.Devices;
using Tortoise.Core.Transactions;
using Tortoise.Core.Updates;

namespace Tortoise.Core.Planning;

public sealed record BrokerPlanValidationOptions(
    int SessionId,
    string CapabilityToken,
    string? PipeName = null,
    int ConnectTimeoutMs = 5000,
    string? DatabasePath = null,
    int? AuthorizedClientProcessId = null);

public sealed record BrokerPlanValidationResult(
    bool Succeeded,
    string Message,
    string? ErrorCode = null);

public interface IBrokerPlanValidationClient
{
    Task<BrokerPlanValidationResult> ValidatePlanAsync(
        Guid planId,
        string planHash,
        BrokerPlanValidationOptions options,
        CancellationToken cancellationToken = default);
}

public sealed record SimulatedMutationWorkflowOptions(
    bool RequireBrokerValidation = false,
    BrokerPlanValidationOptions? BrokerOptions = null);

public sealed record SimulatedMutationWorkflowResult(
    UpdateTransactionRecord Transaction,
    UpdatePreflight Preflight,
    BrokerPlanValidationResult? BrokerValidation,
    UpdateVerification PackageVerification,
    UpdateVerification PostInstallVerification,
    bool CompletedSuccessfully,
    string Summary);

public interface ISimulatedMutationWorkflowService
{
    Task<SimulatedMutationWorkflowResult> RunAsync(
        Guid planId,
        SimulatedMutationWorkflowOptions options,
        CancellationToken cancellationToken = default);
}

public interface ISimulatedUpdatePackageVerificationService
{
    UpdateVerification VerifyPackage(StoredUpdatePlan storedPlan, Guid transactionId);
}

public interface ISimulatedPostInstallVerificationService
{
    UpdateVerification VerifyPostInstall(
        StoredUpdatePlan storedPlan,
        DeviceInventoryEntry deviceBefore,
        DeviceInventoryEntry deviceAfter,
        Guid transactionId);
}
