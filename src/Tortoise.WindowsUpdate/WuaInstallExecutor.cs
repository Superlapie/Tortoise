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
            var searcher = session.CreateUpdateSearcher();
            var criteria =
                $"UpdateID='{updateId}' and RevisionNumber={revision.ToString(CultureInfo.InvariantCulture)}";

            ISearchResult searchResult;
            try
            {
                searchResult = searcher.Search(criteria);
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
                WuaComFactory.ReleaseComObject(searchResult);
                WuaComFactory.ReleaseComObject(searcher);
                return new WindowsUpdateInstallResult(
                    false,
                    ResultCode: (int)OperationResultCode.Failed,
                    RebootRequired: false,
                    "Windows Update did not return the requested driver package.");
            }

            var update = searchResult.Updates[0];
            var collection = WuaComFactory.CreateUpdateCollection();
            collection.Add(update);

            var downloader = session.CreateUpdateDownloader();
            downloader.Updates = collection;

            IDownloadResult downloadResult;
            try
            {
                downloadResult = downloader.Download();
            }
            catch (COMException ex)
            {
                throw new TortoiseException(
                    TortoiseErrorCategory.WindowsUpdateError,
                    "Windows Update driver download failed.",
                    $"WUA download failed: {ex.Message}",
                    ex.HResult,
                    ex);
            }

            if (downloadResult.ResultCode is not OperationResultCode.Succeeded
                and not OperationResultCode.SucceededWithErrors)
            {
                var downloadCode = (int)downloadResult.ResultCode;
                WuaComFactory.ReleaseComObject(downloadResult);
                WuaComFactory.ReleaseComObject(downloader);
                WuaComFactory.ReleaseComObject(collection);
                WuaComFactory.ReleaseComObject(update);
                WuaComFactory.ReleaseComObject(searchResult);
                WuaComFactory.ReleaseComObject(searcher);

                return new WindowsUpdateInstallResult(
                    false,
                    downloadCode,
                    RebootRequired: false,
                    $"Windows Update download returned result code {downloadCode.ToString(CultureInfo.InvariantCulture)} (HRESULT 0x{downloadResult.HResult:X8}).");
            }

            WuaComFactory.ReleaseComObject(downloadResult);

            var installer = session.CreateUpdateInstaller();
            installer.Updates = collection;

            if (installer is IUpdateInstaller2 installer2)
            {
                installer2.ForceQuiet = true;
            }

            IInstallationResult installationResult;
            try
            {
                installationResult = installer.Install();
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

            var resultCode = (int)installationResult.ResultCode;
            var rebootRequired = installationResult.RebootRequired || installer.RebootRequiredBeforeInstallation;
            if (update is IUpdate2 update2)
            {
                rebootRequired |= update2.RebootRequired;
            }

            var succeeded = installationResult.ResultCode is OperationResultCode.Succeeded
                or OperationResultCode.SucceededWithErrors;
            var message = succeeded
                ? "Windows Update reported that driver download and installation completed."
                : $"Windows Update install returned result code {resultCode.ToString(CultureInfo.InvariantCulture)} (HRESULT 0x{installationResult.HResult:X8}).";

            WuaComFactory.ReleaseComObject(installationResult);
            WuaComFactory.ReleaseComObject(installer);
            WuaComFactory.ReleaseComObject(downloader);
            WuaComFactory.ReleaseComObject(collection);
            WuaComFactory.ReleaseComObject(update);
            WuaComFactory.ReleaseComObject(searchResult);
            WuaComFactory.ReleaseComObject(searcher);

            return new WindowsUpdateInstallResult(succeeded, resultCode, rebootRequired, message);
        });
    }
}
