namespace Tortoise.Core.Updates;

public sealed record WindowsUpdatePolicyInfo(
    bool IsManaged,
    string SourceDescription,
    string DisplayMessage);

public sealed record WindowsUpdateCandidate(
    string UpdateId,
    int Revision,
    string Title,
    string? Description,
    string? DriverManufacturer,
    string? DriverClass,
    string? DriverModel,
    Version? DriverVersion,
    DateOnly? DriverDate,
    UpdateClassification Classification,
    bool RestartRequired,
    bool RequiresEula,
    bool IsHidden,
    bool IsInstalled,
    IReadOnlyList<string> Categories,
    string? DriverHardwareId = null,
    string? DriverProvider = null);

public sealed record WindowsUpdateCandidateDetails(
    WindowsUpdateCandidate Candidate,
    string? MoreInfoUrl,
    string? SupportUrl,
    DateTimeOffset ScannedAtUtc);

public sealed record DriverUpdateScanResult(
    IReadOnlyList<WindowsUpdateCandidate> Candidates,
    WindowsUpdatePolicyInfo Policy,
    IReadOnlyList<string> Warnings,
    DateTimeOffset ScannedAtUtc);

public static class WindowsUpdateSearchCriteria
{
    public const string DriverUpdates =
        "IsInstalled=0 and Type='Driver' and IsHidden=0";
}

public static class WindowsUpdateClassificationMapper
{
    public static UpdateClassification Classify(
        int autoSelection,
        bool isHidden,
        bool isInstalled)
    {
        if (isInstalled)
        {
            return UpdateClassification.Current;
        }

        if (isHidden)
        {
            return UpdateClassification.Restricted;
        }

        return autoSelection switch
        {
            3 => UpdateClassification.WindowsRecommended,
            2 => UpdateClassification.WindowsOptional,
            1 => UpdateClassification.ReviewRequired,
            _ => UpdateClassification.ReviewRequired,
        };
    }

    public static WindowsUpdatePolicyInfo DescribePolicy(int serverSelection)
    {
        return serverSelection switch
        {
            1 => new WindowsUpdatePolicyInfo(
                true,
                "Managed server (WSUS)",
                "Update source: Managed by your organization. Tortoise is respecting the Windows Update policy configured on this PC."),
            0 => new WindowsUpdatePolicyInfo(
                false,
                "Default (Group Policy)",
                "Update source: Windows Update policy configured on this PC."),
            2 => new WindowsUpdatePolicyInfo(
                false,
                "Windows Update",
                "Update source: Windows Update."),
            _ => new WindowsUpdatePolicyInfo(
                false,
                "Other configured source",
                "Update source: Windows Update policy configured on this PC."),
        };
    }
}
