using System.Runtime.InteropServices;
using Tortoise.WindowsUpdate.Interop;

namespace Tortoise.WindowsUpdate;

internal static class WuaComFactory
{
    internal const string UpdateSessionProgId = "Microsoft.Update.Session";
    internal const string UpdateCollectionProgId = "Microsoft.Update.UpdateColl";

    internal static IUpdateSession CreateSession()
    {
        var type = Type.GetTypeFromProgID(UpdateSessionProgId, throwOnError: true)
            ?? throw new InvalidOperationException(
                $"Windows Update ProgID '{UpdateSessionProgId}' is not registered on this PC.");

        var instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException(
                $"Windows Update failed to activate ProgID '{UpdateSessionProgId}'.");

        return (IUpdateSession)instance;
    }

    internal static IUpdateCollection CreateUpdateCollection()
    {
        var type = Type.GetTypeFromProgID(UpdateCollectionProgId, throwOnError: true)
            ?? throw new InvalidOperationException(
                $"Windows Update ProgID '{UpdateCollectionProgId}' is not registered on this PC.");

        var instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException(
                $"Windows Update failed to activate ProgID '{UpdateCollectionProgId}'.");

        return (IUpdateCollection)instance;
    }

    internal static void ReleaseComObject(object? comObject)
    {
        if (comObject is not null)
        {
            Marshal.ReleaseComObject(comObject);
        }
    }
}
