using System.Reflection;

namespace Tortoise.App.ViewModels;

public sealed partial class AboutViewModel : PageViewModelBase
{
    public string ApplicationName { get; } = "Tortoise";

    public string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0-alpha";

    public string Tagline { get; } = "Transparent Windows driver management.";

    public string Description { get; } =
        "Know what is installed. Know what Windows recommends. Know what changes before you approve them.";

    public string SafetyNotice { get; } =
        "Read-only alpha. System mutation is disabled during early development.";
}
