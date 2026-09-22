using System.Text.Json;
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

    public static IReadOnlyList<string> GetAllowedOperationNames(bool includeDriverInstall = false)
    {
        var operations = AllowedOperations.ToList();
        if (includeDriverInstall)
        {
            operations.Add(BrokerOperation.InstallDriver);
        }

        return operations
            .Select(operation => operation.ToString())
            .OrderBy(name => name)
            .ToList();
    }
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
    private readonly string _expectedCapabilityToken;

    public BrokerRequestValidator(string expectedCapabilityToken)
    {
        _expectedCapabilityToken = expectedCapabilityToken;
    }

    public BrokerRequestValidationResult Validate(
        BrokerRequest request,
        int expectedSessionId,
        BrokerReplayGuard replayGuard,
        bool allowDriverInstall = false)
    {
        if (request.ProtocolVersion != ElevationConstants.Version)
        {
            return Invalid(BrokerErrorCode.InvalidRequest, "Unsupported broker protocol version.");
        }

        if (request.SessionId != expectedSessionId)
        {
            return Invalid(BrokerErrorCode.SessionMismatch, "Broker Windows session identifier does not match.");
        }

        if (string.IsNullOrWhiteSpace(request.CapabilityToken)
            || !string.Equals(request.CapabilityToken, _expectedCapabilityToken, StringComparison.Ordinal))
        {
            return Invalid(BrokerErrorCode.UnauthorizedClient, "Broker capability token is missing or invalid.");
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
            if (!allowDriverInstall)
            {
                return Invalid(
                    BrokerErrorCode.MutationDisabled,
                    "Driver installation through the broker is not enabled.");
            }

            return ValidateInstallDriverRequest(request);
        }

        if (!BrokerOperationAllowlist.IsAllowed(request.Operation))
        {
            return Invalid(BrokerErrorCode.OperationNotAllowed, "Broker operation is not allowlisted.");
        }

        if (request.Operation == BrokerOperation.ValidatePlan)
        {
            return ValidatePlanRequest(request);
        }

        return new BrokerRequestValidationResult(true, BrokerErrorCode.None, "Request accepted.");
    }

    private static BrokerRequestValidationResult ValidatePlanRequest(BrokerRequest request)
    {
        if (request.PlanId is null || string.IsNullOrWhiteSpace(request.PlanHash))
        {
            return Invalid(BrokerErrorCode.PlanValidationFailed, "Plan validation requires plan id and hash.");
        }

        if (!IsValidPlanHash(request.PlanHash))
        {
            return Invalid(BrokerErrorCode.PlanValidationFailed, "Plan hash format is invalid.");
        }

        return new BrokerRequestValidationResult(true, BrokerErrorCode.None, "Request accepted.");
    }

    private static BrokerRequestValidationResult ValidateInstallDriverRequest(BrokerRequest request)
    {
        var planValidation = ValidatePlanRequest(request);
        if (!planValidation.IsValid)
        {
            return planValidation;
        }

        if (string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            return Invalid(
                BrokerErrorCode.PlanValidationFailed,
                "Driver install requires a payload with update identity.");
        }

        try
        {
            var payload = JsonSerializer.Deserialize<BrokerInstallPayload>(request.PayloadJson);
            if (payload is null
                || string.IsNullOrWhiteSpace(payload.UpdateId)
                || payload.Revision <= 0)
            {
                return Invalid(
                    BrokerErrorCode.PlanValidationFailed,
                    "Driver install payload is invalid.");
            }
        }
        catch (JsonException)
        {
            return Invalid(
                BrokerErrorCode.PlanValidationFailed,
                "Driver install payload could not be parsed.");
        }

        return new BrokerRequestValidationResult(true, BrokerErrorCode.None, "Request accepted.");
    }

    private static bool IsValidPlanHash(string planHash) =>
        planHash.Length == 64 && planHash.All(static character => char.IsAsciiHexDigit(character));

    private static BrokerRequestValidationResult Invalid(BrokerErrorCode errorCode, string message) =>
        new(false, errorCode, message);
}

public static class BrokerRequestFactory
{
    public static BrokerRequest Create(
        BrokerOperation operation,
        int sessionId,
        string capabilityToken,
        Guid? planId = null,
        string? planHash = null,
        string? payloadJson = null) =>
        new(
            ElevationConstants.Version,
            Guid.NewGuid(),
            Guid.NewGuid(),
            sessionId,
            operation,
            planId,
            planHash,
            DateTimeOffset.UtcNow,
            payloadJson,
            capabilityToken);

    public static BrokerRequest CreateInstallDriver(
        int sessionId,
        string capabilityToken,
        Guid planId,
        string planHash,
        string updateId,
        int revision)
    {
        var payloadJson = JsonSerializer.Serialize(new BrokerInstallPayload(updateId, revision));
        return Create(
            BrokerOperation.InstallDriver,
            sessionId,
            capabilityToken,
            planId,
            planHash,
            payloadJson);
    }
}
