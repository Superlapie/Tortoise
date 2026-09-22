using System.Collections.ObjectModel;
using Tortoise.App.Services;

namespace Tortoise.App.ViewModels;

public sealed partial class HistoryViewModel : PageViewModelBase
{
    public ObservableCollection<HistoryItemViewModel> Items { get; } = [];

    public void Load(IReadOnlyList<ScanSessionSnapshot> history)
    {
        Items.Clear();
        foreach (var entry in history)
        {
            Items.Add(HistoryItemViewModel.FromSnapshot(entry));
        }
    }
}

public sealed class HistoryItemViewModel
{
    public required DateTimeOffset TimestampUtc { get; init; }

    public required string Summary { get; init; }

    public static HistoryItemViewModel FromSnapshot(ScanSessionSnapshot snapshot)
    {
        var recommendations = snapshot.Result.Recommendations;
        var recommended = recommendations.Count(item =>
            item.Classification == Core.Updates.UpdateClassification.WindowsRecommended);
        var problems = recommendations.Count(item =>
            item.Device.Snapshot.Health.State == Core.Devices.DeviceHealthState.Problem);

        return new HistoryItemViewModel
        {
            TimestampUtc = snapshot.ScannedAtUtc,
            Summary =
                $"Session {snapshot.SessionId.ToString()}: {recommendations.Count.ToString()} devices, {problems.ToString()} problems, {recommended.ToString()} recommended updates",
        };
    }
}
