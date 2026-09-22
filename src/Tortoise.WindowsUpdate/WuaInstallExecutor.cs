using System.Globalization;
using System.Runtime.InteropServices;
using Tortoise.Core.Errors;
using Tortoise.Core.Installation;
using Tortoise.WindowsUpdate.Interop;

namespace Tortoise.WindowsUpdate;

internal static class WuaInstallExecutor
{
    internal static WindowsUpdateInstallResult Install(
        string updateId,
        int revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return WuaSessionRunner.Execute(session =>
        {
            var searcher = (IUpdateSearcher)session.CreateUpdateSearcher();
            var criteria =
                $"UpdateID='{updateId}' and RevisionNumber={revision.ToString(CultureInfo.InvariantCulture)}";

            ISearchResult searchResult;
            try
            {
                searchResult = (ISearchResult)searcher.Search(criteria);
            }
            catch (COMException ex)
            {
                throw new TortoiseException(
                    TortoiseErrorCategory.WindowsUpdateError,
                    "Windows Update could not locate the requested driver package.",
                    $"WUA install search failed: {ex.Message}",
                    ex.HResult,
                    ex);
            }

            if (searchResult.Updates.Count == 0)
            {
                Marshal.ReleaseComObject(searchResult);
                Marshal.ReleaseComObject(searcher);
                return new WindowsUpdateInstallResult(
                    false,
                    ResultCode: 4,
                    RebootRequired: false,
                    "Windows Update did not return the requested driver package.");
            }

            var update = searchResult.Updates[1];
            var installer = (IUpdateInstaller)session.CreateUpdateInstaller();
            var collection = (IUpdateCollection)session.CreateUpdateCollection();
            collection.Add(update);
            installer.Updates = collection;
            installer.ForceQuiet = true;

            int resultCode;
            try
            {
                resultCode = installer.Install();
            }
            catch (COMException ex)
            {
                throw new TortoiseException(
                    TortoiseErrorCategory.WindowsUpdateError,
                    "Windows Update driver installation failed.",
                    $"WUA install failed: {ex.Message}",
                    ex.HResult,
                    ex);
            }

            var rebootRequired = update.RebootRequired;
            if (installer is IUpdateInstaller2 installer2)
            {
                rebootRequired |= installer2.RebootRequiredBeforeInstallation;
            }

            var succeeded = resultCode is 2 or 3;
            var message = succeeded
                ? "Windows Update reported that driver installation completed."
                : $"Windows Update install returned result code {resultCode.ToString(CultureInfo.InvariantCulture)}.";

            Marshal.ReleaseComObject(collection);
            Marshal.ReleaseComObject(installer);
            Marshal.ReleaseComObject(update);
            Marshal.ReleaseComObject(searchResult);
            Marshal.ReleaseComObject(searcher);

            return new WindowsUpdateInstallResult(succeeded, resultCode, rebootRequired, message);
        });
    }
}
