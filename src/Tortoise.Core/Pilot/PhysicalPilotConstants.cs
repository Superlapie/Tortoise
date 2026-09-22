namespace Tortoise.Core.Pilot;

public static class PhysicalPilotConstants
{
    public const string ConfirmationPhrase = "APPROVE PHYSICAL PILOT";

    public static readonly TimeSpan RecoveryPreparationMaxAge = TimeSpan.FromHours(24);

    public static readonly IReadOnlyList<string> RequiredAcknowledgementIds =
    [
        "recovery-docs-reviewed",
        "restart-impact-understood",
        "single-device-scope-understood",
    ];
}
