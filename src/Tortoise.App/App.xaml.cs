using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tortoise.App.Services;
using Tortoise.App.ViewModels;
using Tortoise.Persistence.Extensions;
using Tortoise.Windows.Extensions;

namespace Tortoise.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(static services =>
            {
                services.AddTortoiseRecommendations();
                services.AddTortoisePersistence();
                services.AddTortoisePhysicalPilotReadiness();
                services.AddSingleton<AppSettings>();
                services.AddSingleton<AppSessionState>();
                services.AddSingleton<ScanCoordinator>();
                services.AddSingleton<OverviewViewModel>();
                services.AddSingleton<DevicesViewModel>();
                services.AddSingleton<UpdatesViewModel>();
                services.AddSingleton<SafetyViewModel>();
                services.AddSingleton<PilotViewModel>();
                services.AddSingleton<HistoryViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<AboutViewModel>();
                services.AddSingleton<MainViewModel>();
            })
            .Build();

        await _host.StartAsync();

        var mainViewModel = _host.Services.GetRequiredService<MainViewModel>();

        var mainWindow = new MainWindow(mainViewModel);
        mainWindow.Show();

        await mainViewModel.RunInitialScanAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    public void ApplyTheme(bool useDarkTheme)
    {
        var themeUri = new Uri(
            useDarkTheme ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml",
            UriKind.Relative);

        var themeDictionary = new ResourceDictionary { Source = themeUri };
        Resources.MergedDictionaries[0] = themeDictionary;
    }
}
