using System.Collections.ObjectModel;
using Tortoise.Core.Devices;
using Tortoise.Core.Recommendations;
using Tortoise.Core.Updates;

namespace Tortoise.App.ViewModels;

public sealed partial class DevicesViewModel : PageViewModelBase
{
    private string _filterText = string.Empty;
    private DeviceListItemViewModel? _selectedDevice;

    public ObservableCollection<DeviceListItemViewModel> Devices { get; } = [];

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                ApplyFilter();
            }
        }
    }

    public DeviceListItemViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set => SetProperty(ref _selectedDevice, value);
    }

    private readonly List<DeviceListItemViewModel> _allDevices = [];

    public void Load(RecommendationScanResult result)
    {
        _allDevices.Clear();
        foreach (var recommendation in result.Recommendations.OrderBy(entry => entry.Device.Snapshot.Identity.ClassName))
        {
            _allDevices.Add(DeviceListItemViewModel.FromRecommendation(recommendation));
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Devices.Clear();
        var filtered = string.IsNullOrWhiteSpace(FilterText)
            ? _allDevices
            : _allDevices.Where(device => device.Matches(FilterText)).ToList();

        foreach (var device in filtered)
        {
            Devices.Add(device);
        }
    }
}

public sealed partial class DeviceListItemViewModel : ObservableObject
{
    public required string FriendlyName { get; init; }

    public required string ClassName { get; init; }

    public required string Manufacturer { get; init; }

    public required string DriverSummary { get; init; }

    public required string VersionSummary { get; init; }

    public required string StatusSummary { get; init; }

    public required string UpdateSummary { get; init; }

    public required string InstanceId { get; init; }

    public required string HardwareIds { get; init; }

    public required string ProblemDetails { get; init; }

    public static DeviceListItemViewModel FromRecommendation(DeviceUpdateRecommendation recommendation)
    {
        var identity = recommendation.Device.Snapshot.Identity;
        var health = recommendation.Device.Snapshot.Health;
        var binding = recommendation.Device.InstalledDriver;

        return new DeviceListItemViewModel
        {
            FriendlyName = identity.FriendlyName,
            ClassName = identity.ClassName,
            Manufacturer = identity.Manufacturer,
            DriverSummary = binding?.ProviderName ?? "Unknown",
            VersionSummary = binding?.DriverVersion?.ToString() ?? "Unknown",
            StatusSummary = health.State.ToString(),
            UpdateSummary = recommendation.Summary,
            InstanceId = identity.DeviceInstanceId,
            HardwareIds = string.Join(Environment.NewLine, identity.HardwareIds),
            ProblemDetails = health.ProblemDescription ?? "No problem reported.",
        };
    }

    public bool Matches(string filter)
    {
        return FriendlyName.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || ClassName.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || Manufacturer.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || InstanceId.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
