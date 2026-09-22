using Tortoise.WindowsUpdate.Interop;

namespace Tortoise.WindowsUpdate;

internal static class WuaBrowseOnlyPolicy
{
    internal static bool TryGetBrowseOnly(IUpdate update, out bool browseOnly)
    {
        browseOnly = false;

        if (update is IWindowsDriverUpdate3 driverUpdate3)
        {
            browseOnly = driverUpdate3.BrowseOnly;
            return true;
        }

        if (update is IUpdate3 update3)
        {
            browseOnly = update3.BrowseOnly;
            return true;
        }

        return false;
    }

    internal static bool IsMutationInstallable(IUpdate update)
    {
        if (!TryGetBrowseOnly(update, out var browseOnly))
        {
            return false;
        }

        return !browseOnly;
    }
}
