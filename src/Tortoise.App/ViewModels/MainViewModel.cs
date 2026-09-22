using System.Collections.ObjectModel;
using Tortoise.App.Services;
using Tortoise.Core.Recommendations;

namespace Tortoise.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppSessionState _sessionState;
    private readonly AppSettings _settings;
    private readonly ScanCoordinator _scanCoordinator;
    private readonly OverviewViewModel _overview;
    private readonly DevicesViewModel _devices;
    private readonly UpdatesViewModel _updates;
    private readonly SafetyViewModel _safety;
    private readonly PilotViewModel _pilot;
    private readonly HistoryViewModel _history;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly AboutViewModel _about;

    private PageViewModelBase _currentPage;
    private bool _isScanning;

    public MainViewModel(
        AppSessionState sessionState,
        AppSettings settings,
        ScanCoordinator scanCoordinator,
        OverviewViewModel overview,
        DevicesViewModel devices,
        UpdatesViewModel updates,
        SafetyViewModel safety,
        PilotViewModel pilot,
        HistoryViewModel history,
        SettingsViewModel settingsViewModel,
        AboutViewModel about)
    {
        _sessionState = sessionState;
        _settings = settings;
        _scanCoordinator = scanCoordinator;
        _overview = overview;
        _devices = devices;
        _updates = updates;
        _safety = safety;
        _pilot = pilot;
        _history = history;
        _settingsViewModel = settingsViewModel;
        _about = about;
        _currentPage = overview;

        NavigationItems =
        [
            new NavigationItem("Overview", overview, NavigateOverviewCommand),
            new NavigationItem("Devices", devices, NavigateDevicesCommand),
            new NavigationItem("Updates", updates, NavigateUpdatesCommand),
            new NavigationItem("Safety", safety, NavigateSafetyCommand),
            new NavigationItem("Pilot", pilot, NavigatePilotCommand),
            new NavigationItem("History", history, NavigateHistoryCommand),
            new NavigationItem("Settings", settingsViewModel, NavigateSettingsCommand),
            new NavigationItem("About", about, NavigateAboutCommand),
        ];

        _settingsViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(SettingsViewModel.UseDarkTheme))
            {
                ThemeChanged?.Invoke(_settings.UseDarkTheme);
            }

            if (args.PropertyName is nameof(SettingsViewModel.IncludeOptionalUpdates))
            {
                OnPropertyChanged(nameof(IncludeOptionalUpdates));
            }
        };
    }

    public event Action<bool>? ThemeChanged;

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    public PageViewModelBase CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public bool IncludeOptionalUpdates
    {
        get => _settings.IncludeOptionalUpdates;
        set => _settingsViewModel.IncludeOptionalUpdates = value;
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set => SetProperty(ref _isScanning, value);
    }

    [RelayCommand]
    private void NavigateOverview() => CurrentPage = _overview;

    [RelayCommand]
    private void NavigateDevices() => CurrentPage = _devices;

    [RelayCommand]
    private void NavigateUpdates() => CurrentPage = _updates;

    [RelayCommand]
    private void NavigateSafety() => CurrentPage = _safety;

    [RelayCommand]
    private void NavigatePilot()
    {
        CurrentPage = _pilot;
        _ = _pilot.RefreshPlansCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void NavigateHistory() => CurrentPage = _history;

    [RelayCommand]
    private void NavigateSettings() => CurrentPage = _settingsViewModel;

    [RelayCommand]
    private void NavigateAbout() => CurrentPage = _about;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAgainAsync()
    {
        await RunScanAsync();
    }

    public async Task RunInitialScanAsync()
    {
        _safety.Load();
        await _sessionState.InitializeAsync();
        _history.Load(_sessionState.History);
        await RunScanAsync();
    }

    private bool CanScan() => !IsScanning;

    private async Task RunScanAsync()
    {
        IsScanning = true;
        ScanAgainCommand.NotifyCanExecuteChanged();
        ClearPageMessages();

        try
        {
            var result = await _scanCoordinator.ScanAsync(
                new RecommendationOptions(IncludeOptionalUpdates: _settings.IncludeOptionalUpdates));

            ApplyScanResult(result);
            CurrentPage.StatusMessage = "Scan completed.";
        }
        catch (Exception ex)
        {
            CurrentPage.ErrorMessage = ex.Message;
        }
        finally
        {
            IsScanning = false;
            ScanAgainCommand.NotifyCanExecuteChanged();
        }
    }

    private void ClearPageMessages()
    {
        foreach (var item in NavigationItems)
        {
            item.Page.ClearMessages();
        }
    }

    public void ApplyScanResult(RecommendationScanResult result)
    {
        _overview.Load(result);
        _devices.Load(result);
        _updates.Load(result);
        _safety.Load(result);
        _history.Load(_sessionState.History);
    }
}

public sealed class NavigationItem
{
    public NavigationItem(string title, PageViewModelBase page, IRelayCommand navigateCommand)
    {
        Title = title;
        Page = page;
        NavigateCommand = navigateCommand;
    }

    public string Title { get; }

    public PageViewModelBase Page { get; }

    public IRelayCommand NavigateCommand { get; }
}
