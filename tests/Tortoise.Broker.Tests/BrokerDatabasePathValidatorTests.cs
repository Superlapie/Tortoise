using Tortoise.Broker.Validation;
using Tortoise.Contracts.Mutation;

namespace Tortoise.Broker.Tests;

public sealed class BrokerDatabasePathValidatorTests
{
    [Fact]
    public void TryValidate_uses_default_path_under_lab_root_when_unspecified()
    {
        var success = BrokerDatabasePathValidator.TryValidate(null, out var normalizedPath, out var error);

        Assert.True(success, error);
        Assert.NotNull(normalizedPath);
        Assert.StartsWith(TortoiseLabPaths.LabDataRoot, normalizedPath!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidate_rejects_paths_outside_lab_root()
    {
        var outsidePath = OperatingSystem.IsWindows()
            ? @"C:\outside\tortoise.db"
            : "/tmp/outside/tortoise.db";

        var success = BrokerDatabasePathValidator.TryValidate(outsidePath, out _, out var error);

        Assert.False(success);
        Assert.Contains("must be located under", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidate_accepts_paths_under_lab_root()
    {
        var allowedPath = Path.Combine(TortoiseLabPaths.LabDataRoot, "fixture-a", "tortoise.db");

        var success = BrokerDatabasePathValidator.TryValidate(allowedPath, out var normalizedPath, out var error);

        Assert.True(success, error);
        Assert.Equal(Path.GetFullPath(allowedPath), normalizedPath);
    }
}
