using Tortoise.Core.Updates;

namespace Tortoise.Core.Recovery;

public sealed record SystemRestoreInfo(
    bool IsEnabled,
    string StatusMessage);

public interface ISystemEnvironmentProvider
{
    Task<SystemSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}

public interface ISystemRestoreInfoProvider
{
    Task<SystemRestoreInfo> GetInfoAsync(CancellationToken cancellationToken = default);
}
