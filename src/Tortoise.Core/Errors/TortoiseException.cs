namespace Tortoise.Core.Errors;

public enum TortoiseErrorCategory
{
    InteropError = 0,
    WindowsUpdateError = 1,
    DeviceDisappearedError = 2,
    PermissionError = 3,
    PolicyBlockedError = 4,
    PreflightBlockedError = 5,
    SignatureError = 6,
    ApplicabilityError = 7,
    StalePlanError = 8,
    BrokerAuthenticationError = 9,
    InstallError = 10,
    VerificationError = 11,
    DatabaseError = 12,
    MutationDeniedError = 13,
}

public sealed class TortoiseException : Exception
{
    public TortoiseException(
        TortoiseErrorCategory category,
        string userMessage,
        string diagnosticMessage,
        int? nativeCode = null,
        Exception? innerException = null)
        : base(userMessage, innerException)
    {
        Category = category;
        UserMessage = userMessage;
        DiagnosticMessage = diagnosticMessage;
        NativeCode = nativeCode;
    }

    public TortoiseErrorCategory Category { get; }

    public string UserMessage { get; }

    public string DiagnosticMessage { get; }

    public int? NativeCode { get; }
}
