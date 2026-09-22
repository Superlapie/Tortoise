namespace Tortoise.Contracts.Elevation;

public enum BrokerOperation
{
    Ping = 0,
    GetStatus = 1,
    ValidatePlan = 2,
    InstallDriver = 3,
}

public enum BrokerErrorCode
{
    None = 0,
    InvalidRequest = 1,
    ReplayDetected = 2,
    SessionMismatch = 3,
    OperationNotAllowed = 4,
    PlanValidationFailed = 5,
    MutationDisabled = 6,
    InternalError = 7,
}

public static class ElevationConstants
{
    public const int Version = 1;

    public const string PipeNamePrefix = "Tortoise.Broker.";

    public static string GetPipeName(int sessionId) => $"{PipeNamePrefix}{sessionId.ToString()}";
}

public sealed record BrokerRequest(
    int ProtocolVersion,
    Guid RequestId,
    Guid Nonce,
    int SessionId,
    BrokerOperation Operation,
    Guid? PlanId,
    string? PlanHash,
    DateTimeOffset IssuedAtUtc,
    string? PayloadJson = null);

public sealed record BrokerInstallPayload(
    string UpdateId,
    int Revision);

public sealed record BrokerResponse(
    Guid RequestId,
    bool Succeeded,
    BrokerErrorCode ErrorCode,
    string Message,
    string? PayloadJson = null);

public sealed record BrokerStatusPayload(
    string BrokerVersion,
    bool MutationEnabled,
    bool DriverInstallEnabled,
    IReadOnlyList<string> AllowedOperations);
