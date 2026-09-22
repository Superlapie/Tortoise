using System.Collections.ObjectModel;
using Tortoise.Core.Recommendations;
using Tortoise.Core.Updates;

namespace Tortoise.App.ViewModels;

public sealed partial class UpdatesViewModel : PageViewModelBase
{
    public ObservableCollection<UpdateGroupViewModel> Groups { get; } = [];

    public void Load(RecommendationScanResult result)
    {
        Groups.Clear();

        AddGroup(
            "Recommended by Windows",
            result.Recommendations.Where(recommendation =>
                recommendation.Classification == UpdateClassification.WindowsRecommended));
        AddGroup(
            "Optional from Windows",
            result.Recommendations.Where(recommendation =>
                recommendation.Classification == UpdateClassification.WindowsOptional));
        AddGroup(
            "Review required",
            result.Recommendations.Where(recommendation =>
                recommendation.Classification == UpdateClassification.ReviewRequired));
        AddGroup(
            "Restricted",
            result.Recommendations.Where(recommendation =>
                recommendation.Classification == UpdateClassification.Restricted));
    }

    private void AddGroup(string title, IEnumerable<DeviceUpdateRecommendation> recommendations)
    {
        var items = recommendations
            .Select(UpdateItemViewModel.FromRecommendation)
            .OrderBy(item => item.DeviceName)
            .ToList();

        if (items.Count == 0)
        {
            return;
        }

        Groups.Add(new UpdateGroupViewModel
        {
            Title = title,
            Items = new ObservableCollection<UpdateItemViewModel>(items),
        });
    }
}

public sealed partial class UpdateGroupViewModel : ObservableObject
{
    public required string Title { get; init; }

    public required ObservableCollection<UpdateItemViewModel> Items { get; init; }
}

public sealed partial class UpdateItemViewModel : ObservableObject
{
    public required string DeviceName { get; init; }

    public required string Classification { get; init; }

    public required string RiskLevel { get; init; }

    public required string CurrentVersion { get; init; }

    public required string ProposedTitle { get; init; }

    public required string Source { get; init; }

    public required string Explanation { get; init; }

    public required bool RestartRequired { get; init; }

    public static UpdateItemViewModel FromRecommendation(DeviceUpdateRecommendation recommendation)
    {
        var binding = recommendation.Device.InstalledDriver;
        return new UpdateItemViewModel
        {
            DeviceName = recommendation.Device.Snapshot.Identity.FriendlyName,
            Classification = recommendation.Summary,
            RiskLevel = recommendation.RiskLevel.ToString(),
            CurrentVersion = binding?.DriverVersion?.ToString() ?? "Unknown",
            ProposedTitle = recommendation.ApplicableUpdate?.Title ?? "No package selected",
            Source = "Windows Update",
            Explanation = recommendation.Explanation,
            RestartRequired = recommendation.ApplicableUpdate?.RestartRequired ?? false,
        };
    }
}
