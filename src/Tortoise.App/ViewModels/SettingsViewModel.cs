using Tortoise.App.Services;

namespace Tortoise.App.ViewModels;

public sealed partial class SettingsViewModel : PageViewModelBase
{
    private readonly AppSettings _settings;

    public SettingsViewModel(AppSettings settings)
    {
        _settings = settings;
    }

    public bool UseDarkTheme
    {
        get => _settings.UseDarkTheme;
        set
        {
            if (_settings.UseDarkTheme != value)
            {
                _settings.UseDarkTheme = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IncludeOptionalUpdates
    {
        get => _settings.IncludeOptionalUpdates;
        set
        {
            if (_settings.IncludeOptionalUpdates != value)
            {
                _settings.IncludeOptionalUpdates = value;
                OnPropertyChanged();
            }
        }
    }

    public string DiagnosticsVerbosity { get; } = "Information";

    public string PrivacySummary { get; } = "Local only. No telemetry is collected by default.";
}
