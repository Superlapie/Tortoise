using Tortoise.Core.Devices;
using Tortoise.Core.Recommendations;
using Tortoise.Core.Updates;

namespace Tortoise.App.ViewModels;

public sealed partial class OverviewViewModel : PageViewModelBase
{
    private DateTimeOffset? _lastScanAtUtc;
    private int _deviceCount;
    private int _problemCount;
    private int _recommendedCount;
    private int _optionalCount;
    private int _restrictedCount;
    private string _summaryText = "Run a scan to inventory devices and evaluate Windows Update recommendations.";

    public DateTimeOffset? LastScanAtUtc
    {
        get => _lastScanAtUtc;
        private set => SetProperty(ref _lastScanAtUtc, value);
    }

    public int DeviceCount
    {
        get => _deviceCount;
        private set => SetProperty(ref _deviceCount, value);
    }

    public int ProblemCount
    {
        get => _problemCount;
        private set => SetProperty(ref _problemCount, value);
    }

    public int RecommendedCount
    {
        get => _recommendedCount;
        private set => SetProperty(ref _recommendedCount, value);
    }

    public int OptionalCount
    {
        get => _optionalCount;
        private set => SetProperty(ref _optionalCount, value);
    }

    public int RestrictedCount
    {
        get => _restrictedCount;
        private set => SetProperty(ref _restrictedCount, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public void Load(RecommendationScanResult result)
    {
        LastScanAtUtc = result.EvaluatedAtUtc;
        DeviceCount = result.Recommendations.Count;
        ProblemCount = result.Recommendations.Count(recommendation =>
            recommendation.Device.Snapshot.Health.State == DeviceHealthState.Problem);
        RecommendedCount = result.Recommendations.Count(recommendation =>
            recommendation.Classification == UpdateClassification.WindowsRecommended);
        OptionalCount = result.Recommendations.Count(recommendation =>
            recommendation.Classification == UpdateClassification.WindowsOptional);
        RestrictedCount = result.Recommendations.Count(recommendation =>
            recommendation.Classification == UpdateClassification.Restricted);

        if (ProblemCount == 0 && RecommendedCount == 0)
        {
            SummaryText =
                "No device problems detected. No recommended driver updates are currently available through Windows Update.";
        }
        else
        {
            SummaryText = "Scan results are available on the Devices and Updates pages.";
        }
    }
}
