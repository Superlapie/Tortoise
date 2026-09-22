using Tortoise.Core.Updates;

namespace Tortoise.Core.Recommendations;

public static class RecommendationTerminology
{
    public const string CurrentSummary = "Up to date according to Windows Update";

    public const string CurrentExplanation =
        "No newer applicable update was found through the currently selected trusted source.";

    public const string RecommendedSummary = "Windows recommends this update";

    public const string OptionalSummary = "Optional Windows driver update";

    public const string ReviewRequiredSummary = "Review required before any change";

    public const string RestrictedSummary = "Tortoise will not install this update category automatically";

    public const string UnknownSummary = "Driver update status could not be determined safely";

    private static readonly string[] ForbiddenMarketingTerms =
    [
        "latest driver",
        "outdated",
        "critical driver",
        "your computer is at risk",
        "update everything",
    ];

    public static string DescribeClassification(UpdateClassification classification) =>
        classification switch
        {
            UpdateClassification.Current => CurrentSummary,
            UpdateClassification.WindowsRecommended => RecommendedSummary,
            UpdateClassification.WindowsOptional => OptionalSummary,
            UpdateClassification.VendorNewer => "Newer official package detected",
            UpdateClassification.ReviewRequired => ReviewRequiredSummary,
            UpdateClassification.Restricted => RestrictedSummary,
            UpdateClassification.Unknown => UnknownSummary,
            _ => UnknownSummary,
        };

    public static string BuildExplanation(
        UpdateClassification classification,
        WindowsUpdateCandidate? update,
        int matchScore)
    {
        if (classification == UpdateClassification.Current)
        {
            return CurrentExplanation;
        }

        if (update is null)
        {
            return UnknownSummary;
        }

        var baseExplanation = classification switch
        {
            UpdateClassification.WindowsRecommended =>
                "Windows Update reports this package as applicable to this device.",
            UpdateClassification.WindowsOptional =>
                "Windows Update offers this driver as optional for this device.",
            UpdateClassification.ReviewRequired =>
                "An update may apply, but Tortoise requires manual review before presenting it as actionable.",
            UpdateClassification.Restricted =>
                "This update category is restricted by Tortoise safety policy.",
            UpdateClassification.Unknown =>
                "Tortoise could not safely determine whether this update applies.",
            _ => "Windows Update metadata was evaluated for this device.",
        };

        if (matchScore > 0)
        {
            return $"{baseExplanation} Match confidence score: {matchScore.ToString()}.";
        }

        return baseExplanation;
    }

    public static bool ContainsForbiddenMarketingLanguage(string text)
    {
        return ForbiddenMarketingTerms.Any(term =>
            text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
