using System.Globalization;
using System.Runtime.InteropServices;
using Tortoise.Core.Errors;
using Tortoise.Core.Updates;
using Tortoise.WindowsUpdate.Interop;

namespace Tortoise.WindowsUpdate;

internal static class WuaUpdateMapper
{
    internal static WindowsUpdateCandidate Map(IUpdate update)
    {
        var identity = update.Identity;
        var autoSelection = update is IUpdate2 update2 ? update2.AutoSelection : 0;
        var categories = ReadCategories(update.Categories);
        var driverClass = categories.FirstOrDefault(category =>
            category.Contains("driver", StringComparison.OrdinalIgnoreCase));

        string? driverHardwareId = null;
        string? driverProvider = null;
        DateOnly? driverVerDate = null;
        Version? driverVersion = null;

        if (update is IWindowsDriverUpdate driverUpdate)
        {
            driverHardwareId = NullIfEmpty(driverUpdate.DriverHardwareID);
            driverProvider = NullIfEmpty(driverUpdate.DriverProvider);
            driverClass = NullIfEmpty(driverUpdate.DriverClass) ?? driverClass;

            if (driverUpdate.DriverVerDate != default)
            {
                driverVerDate = DateOnly.FromDateTime(driverUpdate.DriverVerDate);
            }
        }

        return new WindowsUpdateCandidate(
            identity.UpdateID,
            identity.RevisionNumber,
            update.Title,
            NullIfEmpty(update.Description),
            NullIfEmpty(update.DriverManufacturer),
            driverClass,
            NullIfEmpty(update.DriverModel),
            driverVersion,
            driverVerDate,
            WindowsUpdateClassificationMapper.Classify(autoSelection, update.IsHidden, update.IsInstalled),
            update.RebootRequired,
            RequiresEula(update),
            update.IsHidden,
            update.IsInstalled,
            categories,
            driverHardwareId,
            driverProvider);
    }

    internal static WindowsUpdateCandidateDetails MapDetails(IUpdate update, WindowsUpdateCandidate candidate) =>
        new(
            candidate,
            NullIfEmpty(update.MoreInfoUrl),
            NullIfEmpty(update.SupportUrl),
            DateTimeOffset.UtcNow);

    private static IReadOnlyList<string> ReadCategories(ICategoryCollection categories)
    {
        var names = new List<string>();
        for (var i = 0; i < categories.Count; i++)
        {
            var name = categories[i].Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static bool RequiresEula(IUpdate update) => !update.EulaAccepted;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

internal static class WuaSessionRunner
{
    internal static T Execute<T>(Func<IUpdateSession, T> action)
    {
        UpdateSession? session = null;
        try
        {
            session = new UpdateSession();
            session.ClientApplicationID = "Tortoise";
            return action(session);
        }
        catch (COMException ex)
        {
            throw new TortoiseException(
                TortoiseErrorCategory.WindowsUpdateError,
                "Windows Update could not be queried on this PC.",
                $"WUA COM activation failed: {ex.Message}",
                ex.HResult,
                ex);
        }
        finally
        {
            if (session is not null)
            {
                Marshal.ReleaseComObject(session);
            }
        }
    }
}

internal static class WuaSearchExecutor
{
    internal static DriverUpdateScanResult Search(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return WuaSessionRunner.Execute(session =>
        {
            var searcher = (IUpdateSearcher)session.CreateUpdateSearcher();
            var policy = WindowsUpdateClassificationMapper.DescribePolicy((int)searcher.ServerSelection);

            cancellationToken.ThrowIfCancellationRequested();

            ISearchResult searchResult;
            try
            {
                searchResult = (ISearchResult)searcher.Search(WindowsUpdateSearchCriteria.DriverUpdates);
            }
            catch (COMException ex)
            {
                throw new TortoiseException(
                    TortoiseErrorCategory.WindowsUpdateError,
                    "Windows Update driver scan failed.",
                    $"WUA search failed: {ex.Message}",
                    ex.HResult,
                    ex);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var warnings = new List<string>();
            if (searchResult.ResultCode != 2)
            {
                warnings.Add(
                    $"Windows Update search completed with result code {searchResult.ResultCode.ToString(CultureInfo.InvariantCulture)}.");
            }

            var candidates = new List<WindowsUpdateCandidate>();
            var updates = searchResult.Updates;
            for (var i = 0; i < updates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var update = updates[i];
                candidates.Add(WuaUpdateMapper.Map(update));
            }

            Marshal.ReleaseComObject(searchResult);
            Marshal.ReleaseComObject(searcher);

            return new DriverUpdateScanResult(
                candidates,
                policy,
                warnings,
                DateTimeOffset.UtcNow);
        });
    }

    internal static WindowsUpdateCandidateDetails? GetDetails(
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
                    "Unable to load Windows Update details.",
                    $"WUA detail search failed: {ex.Message}",
                    ex.HResult,
                    ex);
            }

            if (searchResult.Updates.Count == 0)
            {
                Marshal.ReleaseComObject(searchResult);
                Marshal.ReleaseComObject(searcher);
                return null;
            }

            var update = searchResult.Updates[0];
            var candidate = WuaUpdateMapper.Map(update);
            var details = WuaUpdateMapper.MapDetails(update, candidate);

            Marshal.ReleaseComObject(searchResult);
            Marshal.ReleaseComObject(searcher);
            return details;
        });
    }
}
