using Tortoise.WindowsUpdate;
using Tortoise.WindowsUpdate.Interop;

namespace Tortoise.WindowsUpdate.Tests;

public sealed class WuaBrowseOnlyPolicyTests
{
    [Fact]
    public void IsMutationInstallable_returns_false_when_browse_only_status_is_unknown()
    {
        var update = new TestUpdate();

        Assert.False(WuaBrowseOnlyPolicy.IsMutationInstallable(update));
    }

    [Fact]
    public void IsMutationInstallable_returns_false_for_browse_only_IUpdate3()
    {
        var update = new TestUpdate3 { BrowseOnly = true };

        Assert.False(WuaBrowseOnlyPolicy.IsMutationInstallable(update));
    }

    [Fact]
    public void IsMutationInstallable_returns_true_for_installable_IUpdate3()
    {
        var update = new TestUpdate3 { BrowseOnly = false };

        Assert.True(WuaBrowseOnlyPolicy.IsMutationInstallable(update));
    }

    [Fact]
    public void IsMutationInstallable_prefers_IWindowsDriverUpdate3_when_available()
    {
        var update = new TestDriverUpdate3 { BrowseOnly = true };

        Assert.False(WuaBrowseOnlyPolicy.IsMutationInstallable(update));
    }

    private class TestUpdate : IUpdate
    {
        public string Title => "Test";
        public ICategoryCollection Categories => throw new NotSupportedException();
        public string Description => "Test";
        public bool EulaAccepted => true;
        public IUpdateIdentity Identity => throw new NotSupportedException();
        public bool IsHidden => false;
        public bool IsInstalled => false;
        public bool IsDownloaded => true;
        public string SupportUrl => string.Empty;
    }

    private sealed class TestUpdate3 : TestUpdate, IUpdate2, IUpdate3
    {
        public bool RebootRequired => false;
        public bool BrowseOnly { get; init; }
    }

    private sealed class TestDriverUpdate3 : TestUpdate, IWindowsDriverUpdate3
    {
        public string DriverClass => "Net";
        public string DriverHardwareID => "PCI\\VEN_8086";
        public string DriverManufacturer => "Intel";
        public string DriverModel => "Adapter";
        public string DriverProvider => "Intel";
        public DateTime DriverVerDate => DateTime.UtcNow;
        public bool RebootRequired => false;
        public bool IsPresent => false;
        public bool BrowseOnly { get; init; }
    }
}
