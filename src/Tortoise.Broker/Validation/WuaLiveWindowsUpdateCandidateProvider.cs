using Tortoise.Core.Updates;
using Tortoise.WindowsUpdate;

namespace Tortoise.Broker.Validation;

public sealed class WuaLiveWindowsUpdateCandidateProvider : ILiveWindowsUpdateCandidateProvider
{
    public Task<WindowsUpdateCandidate?> TryGetCandidateAsync(
        string updateId,
        int revision,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(WuaLiveVerification.TryGetCandidate(updateId, revision, cancellationToken));
}
