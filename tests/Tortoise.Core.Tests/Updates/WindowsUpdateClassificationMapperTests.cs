using Tortoise.Core.Updates;

namespace Tortoise.Core.Tests.Updates;

public sealed class WindowsUpdateClassificationMapperTests
{
    [Theory]
    [InlineData(3, false, false, UpdateClassification.WindowsRecommended)]
    [InlineData(2, false, false, UpdateClassification.WindowsOptional)]
    [InlineData(1, false, false, UpdateClassification.ReviewRequired)]
    [InlineData(0, false, false, UpdateClassification.ReviewRequired)]
    [InlineData(0, true, false, UpdateClassification.Restricted)]
    [InlineData(0, false, true, UpdateClassification.Current)]
    public void Classify_maps_wua_auto_selection(
        int autoSelection,
        bool isHidden,
        bool isInstalled,
        UpdateClassification expected)
    {
        Assert.Equal(
            expected,
            WindowsUpdateClassificationMapper.Classify(autoSelection, isHidden, isInstalled));
    }

    [Fact]
    public void DescribePolicy_marks_wsus_as_managed()
    {
        var policy = WindowsUpdateClassificationMapper.DescribePolicy(1);

        Assert.True(policy.IsManaged);
        Assert.Contains("organization", policy.DisplayMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DriverUpdates_search_criteria_excludes_hidden_and_installed()
    {
        Assert.Contains("Type='Driver'", WindowsUpdateSearchCriteria.DriverUpdates, StringComparison.Ordinal);
        Assert.Contains("IsHidden=0", WindowsUpdateSearchCriteria.DriverUpdates, StringComparison.Ordinal);
        Assert.Contains("IsInstalled=0", WindowsUpdateSearchCriteria.DriverUpdates, StringComparison.Ordinal);
    }
}
