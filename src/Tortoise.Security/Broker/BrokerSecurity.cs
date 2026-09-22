using Tortoise.Contracts.Elevation;

namespace Tortoise.Security.Broker;

public static class BrokerOperationAllowlist
{
    private static readonly HashSet<BrokerOperation> AllowedOperations =
    [
        BrokerOperation.Ping,
        BrokerOperation.GetStatus,
        BrokerOperation.ValidatePlan,
    ];

    public static bool IsAllowed(BrokerOperation operation) => AllowedOperations.Contains(operation);

    public static IReadOnlyList<string> GetAllowedOperationNames() =>
        AllowedOperations.Select(operation => operation.ToString()).OrderBy(name => name).ToList();
}

public sealed class BrokerReplayGuard
{
    private readonly HashSet<Guid> _consumedNonces = [];
    private readonly Lock _lock = new();

    public bool TryConsume(Guid nonce)
    {
        lock (_lock)
        {
            return _consumedNonces.Add(nonce);
        }
    }
}

public sealed record BrokerRequestValidationResult(
    bool IsValid,
    BrokerErrorCode ErrorCode,
    string Message);

public sealed class BrokerRequestValidator
{
    private static readonly TimeSpan MaxRequestAge = TimeSpan.FromMinutes(5);

    public BrokerRequestValidationResult Validate(
        BrokerRequest request,
        int expectedSessionId,
        BrokerReplayGuard replayGuard)
    {
        if (request.ProtocolVersion != ElevationConstants.Version)
        {
            return Invalid(BrokerErrorCode.InvalidRequest, "Unsupported broker protocol version.");
        }

        if (request.SessionId != expectedSessionId)
        {
            return Invalid(BrokerErrorCode.SessionMismatch, "Broker session identifier does not match.");
        }

        if (DateTimeOffset.UtcNow - request.IssuedAtUtc > MaxRequestAge)
        {
            return Invalid(BrokerErrorCode.InvalidRequest, "Broker request is too old.");
        }

        if (!replayGuard.TryConsume(request.Nonce))
        {
            return Invalid(BrokerErrorCode.ReplayDetected, "Broker nonce was already consumed.");
        }

        if (request.Operation == BrokerOperation.InstallDriver)
        {
            return Invalid(
                BrokerErrorCode.MutationDisabled,
                "Driver installation through the broker is not enabled.");
        }

        if (!BrokerOperationAllowlist.IsAllowed(request.Operation))
        {
            return Invalid(BrokerErrorCode.OperationNotAllowed, "Broker operation is not allowlisted.");
        }

        if (request.Operation == BrokerOperation.ValidatePlan)
        {
            if (request.PlanId is null || string.IsNullOrWhiteSpace(request.PlanHash))
            {
                return Invalid(BrokerErrorCode.PlanValidationFailed, "Plan validation requires plan id and hash.");
            }

            if (request.PlanHash.Length != 64
                || !request.PlanHash.All(static character =>
                    char.IsAsciiHexDigit(character)))
            {
                return Invalid(BrokerErrorCode.PlanValidationFailed, "Plan hash format is invalid.");
            }
        }

        return new BrokerRequestValidationResult(true, BrokerErrorCode.None, "Request accepted.");
    }

    private static BrokerRequestValidationResult Invalid(BrokerErrorCode errorCode, string message) =>
        new(false, errorCode, message);
}

public static class BrokerRequestFactory
{
    public static BrokerRequest Create(
        BrokerOperation operation,
        int sessionId,
        Guid? planId = null,
        string? planHash = null) =>
        new(
            ElevationConstants.Version,
            Guid.NewGuid(),
            Guid.NewGuid(),
            sessionId,
            operation,
            planId,
            planHash,
            DateTimeOffset.UtcNow);
}
