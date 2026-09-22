using System.Windows;
using Tortoise.App.ViewModels;

namespace Tortoise.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(bool useDarkTheme)
    {
        if (Application.Current is App app)
        {
            app.ApplyTheme(useDarkTheme);
        }
    }
}
