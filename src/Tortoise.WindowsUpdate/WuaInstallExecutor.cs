using System.Globalization;
using System.Runtime.InteropServices;
using Tortoise.Core.Errors;
using Tortoise.Core.Installation;
using Tortoise.WindowsUpdate.Interop;

namespace Tortoise.WindowsUpdate;

internal static class WuaInstallExecutor
{
    internal static WindowsUpdateDownloadResult Download(string updateId, int revision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return WuaSessionRunner.Execute(session =>
        {
            using var scope = new WuaComScope();
            var searchBundle = SearchUpdate(session, updateId, revision, scope);
            if (searchBundle is null)
            {
                return new WindowsUpdateDownloadResult(
                    false,
                    (int)OperationResultCode.Failed,
                    0,
                    "Windows Update did not return the requested driver package.");
            }

            var downloader = session.CreateUpdateDownloader();
            scope.Track(downloader);
            downloader.Updates = searchBundle.Collection;

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

            scope.Track(downloadResult);
            return EvaluateDownloadResult(downloadResult);
        });
    }

    internal static WindowsUpdateInstallResult InstallPrepared(
        string updateId,
        int revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return WuaSessionRunner.Execute(session =>
        {
            using var scope = new WuaComScope();
            var searchBundle = SearchUpdate(session, updateId, revision, scope);
            if (searchBundle is null)
            {
                return new WindowsUpdateInstallResult(
                    false,
                    (int)OperationResultCode.Failed,
                    0,
                    RebootRequired: false,
                    "Windows Update did not return the requested driver package.");
            }

            var installer = session.CreateUpdateInstaller();
            scope.Track(installer);
            installer.Updates = searchBundle.Collection;

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

            scope.Track(installationResult);
            var rebootRequired = installationResult.RebootRequired || installer.RebootRequiredBeforeInstallation;
            if (searchBundle.Update is IUpdate2 update2)
            {
                rebootRequired |= update2.RebootRequired;
            }

            return EvaluateInstallationResult(installationResult, rebootRequired);
        });
    }

    internal static WindowsUpdateInstallResult Install(
        string updateId,
        int revision,
        CancellationToken cancellationToken)
    {
        var download = Download(updateId, revision, cancellationToken);
        if (!download.Succeeded)
        {
            return new WindowsUpdateInstallResult(
                false,
                download.ResultCode,
                download.UpdateHResult,
                RebootRequired: false,
                download.Message);
        }

        return InstallPrepared(updateId, revision, cancellationToken);
    }

    private static SearchBundle? SearchUpdate(
        IUpdateSession session,
        string updateId,
        int revision,
        WuaComScope scope)
    {
        var searcher = session.CreateUpdateSearcher();
        scope.Track(searcher);
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

        scope.Track(searchResult);
        if (searchResult.Updates.Count == 0)
        {
            return null;
        }

        var update = searchResult.Updates[0];
        scope.Track(update);
        var collection = WuaComFactory.CreateUpdateCollection();
        scope.Track(collection);
        collection.Add(update);
        return new SearchBundle(update, collection);
    }

    private static WindowsUpdateDownloadResult EvaluateDownloadResult(IDownloadResult downloadResult)
    {
        var aggregateCode = (int)downloadResult.ResultCode;
        var aggregateHResult = downloadResult.HResult;
        var updateResult = downloadResult.GetUpdateResult(0);
        var updateCode = (int)updateResult.ResultCode;
        var updateHResult = updateResult.HResult;

        var succeeded = downloadResult.ResultCode == OperationResultCode.Succeeded
            && updateResult.ResultCode == OperationResultCode.Succeeded
            && updateHResult >= 0;

        var message = succeeded
            ? "Windows Update reported that the driver package download completed for the requested update."
            : $"Windows Update download failed for the requested update (aggregate={aggregateCode}, update={updateCode}, HRESULT=0x{updateHResult:X8}, aggregate HRESULT=0x{aggregateHResult:X8}).";

        return new WindowsUpdateDownloadResult(succeeded, updateCode, updateHResult, message);
    }

    private static WindowsUpdateInstallResult EvaluateInstallationResult(
        IInstallationResult installationResult,
        bool rebootRequired)
    {
        var aggregateCode = (int)installationResult.ResultCode;
        var aggregateHResult = installationResult.HResult;
        var updateResult = installationResult.GetUpdateResult(0);
        var updateCode = (int)updateResult.ResultCode;
        var updateHResult = updateResult.HResult;

        var succeeded = installationResult.ResultCode == OperationResultCode.Succeeded
            && updateResult.ResultCode == OperationResultCode.Succeeded
            && updateHResult >= 0;

        var message = succeeded
            ? "Windows Update reported that driver installation completed for the requested update."
            : $"Windows Update install failed for the requested update (aggregate={aggregateCode}, update={updateCode}, HRESULT=0x{updateHResult:X8}, aggregate HRESULT=0x{aggregateHResult:X8}).";

        return new WindowsUpdateInstallResult(succeeded, updateCode, updateHResult, rebootRequired, message);
    }

    private sealed record SearchBundle(IUpdate Update, IUpdateCollection Collection);

    private sealed class WuaComScope : IDisposable
    {
        private readonly List<object> _objects = [];

        internal void Track(object comObject)
        {
            if (comObject is not null)
            {
                _objects.Add(comObject);
            }
        }

        public void Dispose()
        {
            for (var index = _objects.Count - 1; index >= 0; index--)
            {
                WuaComFactory.ReleaseComObject(_objects[index]);
            }
        }
    }
}
