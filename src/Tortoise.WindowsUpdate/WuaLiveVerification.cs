using Tortoise.Core.Planning;
using Tortoise.Core.Updates;

namespace Tortoise.WindowsUpdate;

public static class WuaLiveVerification
{
    public static WindowsUpdateCandidate? TryGetCandidate(
        string updateId,
        int revision,
        CancellationToken cancellationToken = default) =>
        WuaSearchExecutor.TryGetCandidate(updateId, revision, cancellationToken);
}
