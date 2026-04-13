using ChartHub.Tests.TestInfrastructure;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public sealed class DesktopEntryViewModelTests
{
    [Fact]
    public void DesktopEntryCardItem_ApplyRunningState_UpdatesFlagsAndPid()
    {
        DesktopEntryCardItem item = new("retro", "RetroArch", "Not running", null, "https://example.test/icon.png");

        item.Apply("Running", 4242);

        Assert.True(item.IsRunning);
        Assert.True(item.CanKill);
        Assert.False(item.CanExecute);
        Assert.Equal("PID: 4242", item.PidLabel);
    }

    [Fact]
    public void DesktopEntryCardItem_ApplyStoppedState_UpdatesFlagsAndPid()
    {
        DesktopEntryCardItem item = new("retro", "RetroArch", "Running", 4242, "https://example.test/icon.png");

        item.Apply("Not running", null);

        Assert.False(item.IsRunning);
        Assert.False(item.CanKill);
        Assert.True(item.CanExecute);
        Assert.Equal("PID: -", item.PidLabel);
    }
}
