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
        var autoSelection = update is IUpdate5 update5
            ? (int)update5.AutoSelection
            : 0;
        var categories = ReadCategories(update.Categories);
        var driverClass = categories.FirstOrDefault(category =>
            category.Contains("driver", StringComparison.OrdinalIgnoreCase));

        string? driverHardwareId = null;
        string? driverProvider = null;
        string? driverManufacturer = null;
        string? driverModel = null;
        DateOnly? driverVerDate = null;

        if (update is IWindowsDriverUpdate driverUpdate)
        {
            driverHardwareId = NullIfEmpty(driverUpdate.DriverHardwareID);
            driverProvider = NullIfEmpty(driverUpdate.DriverProvider);
            driverManufacturer = NullIfEmpty(driverUpdate.DriverManufacturer);
            driverModel = NullIfEmpty(driverUpdate.DriverModel);
            driverClass = NullIfEmpty(driverUpdate.DriverClass) ?? driverClass;

            if (driverUpdate.DriverVerDate != default)
            {
                driverVerDate = DateOnly.FromDateTime(driverUpdate.DriverVerDate);
            }
        }

        var rebootRequired = update is IUpdate2 update2 && update2.RebootRequired;

        return new WindowsUpdateCandidate(
            identity.UpdateID,
            identity.RevisionNumber,
            update.Title,
            NullIfEmpty(update.Description),
            driverManufacturer,
            driverClass,
            driverModel,
            null,
            driverVerDate,
            WindowsUpdateClassificationMapper.Classify(autoSelection, update.IsHidden, update.IsInstalled),
            rebootRequired,
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
            null,
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
        IUpdateSession? session = null;
        try
        {
            session = WuaComFactory.CreateSession();
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
            WuaComFactory.ReleaseComObject(session);
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
            var searcher = session.CreateUpdateSearcher();
            var policy = WindowsUpdateClassificationMapper.DescribePolicy((int)searcher.ServerSelection);

            cancellationToken.ThrowIfCancellationRequested();

            ISearchResult searchResult;
            try
            {
                searchResult = searcher.Search(WindowsUpdateSearchCriteria.DriverUpdates);
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
            if (searchResult.ResultCode != OperationResultCode.Succeeded)
            {
                warnings.Add(
                    $"Windows Update search completed with result code {(int)searchResult.ResultCode}.");
            }

            var candidates = new List<WindowsUpdateCandidate>();
            var updates = searchResult.Updates;
            for (var i = 0; i < updates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidates.Add(WuaUpdateMapper.Map(updates[i]));
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

    internal static WindowsUpdateCandidate? TryGetCandidate(
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
            catch (COMException)
            {
                Marshal.ReleaseComObject(searcher);
                return null;
            }

            if (searchResult.Updates.Count == 0)
            {
                Marshal.ReleaseComObject(searchResult);
                Marshal.ReleaseComObject(searcher);
                return null;
            }

            var candidate = WuaUpdateMapper.Map(searchResult.Updates[0]);

            Marshal.ReleaseComObject(searchResult);
            Marshal.ReleaseComObject(searcher);
            return candidate;
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

public static class WuaHardwareIdMatcher
{
    public static bool MatchesDevice(
        WindowsUpdateCandidate candidate,
        IReadOnlyList<string> hardwareIds,
        IReadOnlyList<string> compatibleIds)
    {
        if (string.IsNullOrWhiteSpace(candidate.DriverHardwareId))
        {
            return false;
        }

        return ContainsId(candidate.DriverHardwareId, hardwareIds)
               || ContainsId(candidate.DriverHardwareId, compatibleIds);
    }

    private static bool ContainsId(string driverHardwareId, IReadOnlyList<string> deviceIds)
    {
        foreach (var deviceId in deviceIds)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                continue;
            }

            if (string.Equals(driverHardwareId, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
