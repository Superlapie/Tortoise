using System.Xml.Linq;
using Tortoise.ReleaseValidation;

namespace Tortoise.ArchitectureTests;

public sealed class ReleaseArchiveContentTests
{
    [Fact]
    public void ReleaseWorkflow_publishes_only_public_app_and_cli()
    {
        var releaseWorkflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "release.yml"));

        Assert.Contains("src/Tortoise.App/Tortoise.App.csproj", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("src/Tortoise.Cli/Tortoise.Cli.csproj", releaseWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Tortoise.Lab", releaseWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Tortoise.LabBroker", releaseWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Tortoise.VmHarness", releaseWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Tortoise.VerificationSummary", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/portable/app", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/portable/cli", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("tools/Tortoise.ReleaseValidation", releaseWorkflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseArchive_must_not_include_developer_only_binaries()
    {
        var forbidden = ReleaseArchiveValidator.ForbiddenEntryNames;

        foreach (var name in forbidden)
        {
            Assert.DoesNotContain(name, File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
                ".github",
                "workflows",
                "release.yml")), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ReleaseArchiveValidator_rejects_forbidden_entry_names()
    {
        Assert.True(ReleaseArchiveValidator.IsForbiddenEntry("portable/cli/tortoise-lab.dll"));
        Assert.True(ReleaseArchiveValidator.IsForbiddenEntry("portable/app/Tortoise.Core.Tests.dll"));
        Assert.False(ReleaseArchiveValidator.IsForbiddenEntry("portable/cli/tortoise.exe"));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Tortoise.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
