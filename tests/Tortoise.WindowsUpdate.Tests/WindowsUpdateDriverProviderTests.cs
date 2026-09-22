using Tortoise.Core.Errors;
using Tortoise.Core.Updates;
using Tortoise.WindowsUpdate;

namespace Tortoise.WindowsUpdate.Tests;

public sealed class WindowsUpdateDriverProviderTests
{
    [Fact]
    public async Task ScanAsync_throws_on_non_windows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var provider = new WindowsUpdateDriverProvider();
        var exception = await Assert.ThrowsAsync<TortoiseException>(() => provider.ScanAsync());

        Assert.Equal(TortoiseErrorCategory.WindowsUpdateError, exception.Category);
    }

    [Fact]
    public async Task ScanAsync_returns_result_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var provider = new WindowsUpdateDriverProvider();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result = await provider.ScanAsync(cts.Token);

        Assert.NotNull(result.Policy);
        Assert.NotNull(result.Candidates);
        Assert.DoesNotContain(result.Candidates, candidate => candidate.IsHidden);
    }

    [Fact]
    public async Task ScanAsync_respects_cancellation_token()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var provider = new WindowsUpdateDriverProvider();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ScanAsync(cts.Token));
    }
}
