using ChartHub.Services;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class SongIngestionStateMachineTests
{
    [Fact]
    public void EnsureCanTransition_ThrowsForInvalidTransition()
    {
        var sut = new SongIngestionStateMachine();

        Assert.Throws<InvalidOperationException>(() => sut.EnsureCanTransition(IngestionState.Queued, IngestionState.Installed));
    }

    [Fact]
    public void CanTransition_AllowsExpectedPath()
    {
        var sut = new SongIngestionStateMachine();

        Assert.True(sut.CanTransition(IngestionState.Queued, IngestionState.ResolvingSource));
        Assert.True(sut.CanTransition(IngestionState.Queued, IngestionState.Downloaded));
        Assert.True(sut.CanTransition(IngestionState.ResolvingSource, IngestionState.Downloading));
        Assert.True(sut.CanTransition(IngestionState.Downloading, IngestionState.Downloaded));
        Assert.True(sut.CanTransition(IngestionState.Downloaded, IngestionState.Converting));
        Assert.True(sut.CanTransition(IngestionState.Converting, IngestionState.Converted));
        Assert.True(sut.CanTransition(IngestionState.Converted, IngestionState.Installing));
        Assert.True(sut.CanTransition(IngestionState.Installing, IngestionState.Installed));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void RetryPolicy_UsesFixedTwoRetryLimit(int retryCount, bool expected)
    {
        bool canRetry = SongIngestionRetryPolicy.CanRetryDownloadFailure(retryCount);

        Assert.Equal(expected, canRetry);
    }
}
