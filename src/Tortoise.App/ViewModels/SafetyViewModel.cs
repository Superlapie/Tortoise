using System.Collections.ObjectModel;
using Tortoise.Contracts.Mutation;
using Tortoise.Core.Recommendations;

namespace Tortoise.App.ViewModels;

public sealed partial class SafetyViewModel : PageViewModelBase
{
    public ObservableCollection<SafetyItemViewModel> Items { get; } = [];

    public void Load(RecommendationScanResult? result = null)
    {
        Items.Clear();
        Items.Add(new SafetyItemViewModel("Driver signatures", "Enforced"));
        Items.Add(new SafetyItemViewModel("Unsigned updates", "Blocked"));
        Items.Add(new SafetyItemViewModel("Firmware updates", "Restricted"));
        Items.Add(new SafetyItemViewModel("Storage drivers", "Restricted"));
        Items.Add(new SafetyItemViewModel("Driver Store cleanup", "Disabled"));
        Items.Add(new SafetyItemViewModel("Microsoft source", "Enabled"));
        Items.Add(new SafetyItemViewModel("Vendor-direct sources", "Disabled"));
        Items.Add(new SafetyItemViewModel("Automatic installation", "Disabled"));
        Items.Add(new SafetyItemViewModel(
            "Mutation capability",
            MutationCapability.ReadOnly.IsEnabled ? "Enabled" : "Disabled"));
        Items.Add(new SafetyItemViewModel(
            "Update source",
            result?.UpdatePolicy.SourceDescription ?? "Not scanned yet"));
        Items.Add(new SafetyItemViewModel(
            "Managed environment",
            result?.UpdatePolicy.IsManaged == true ? "Yes" : "No"));
    }
}

public sealed class SafetyItemViewModel
{
    public SafetyItemViewModel(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string Value { get; }
}
