using ChartHub.Configuration.Migration;

namespace ChartHub.Tests;

[Trait(TestInfrastructure.TestCategories.Category, TestInfrastructure.TestCategories.Unit)]
public class SettingsMigrationResultTests
{
    [Fact]
    public void None_HasNoChanges()
    {
        Assert.False(SettingsMigrationResult.None.HasChanges);
    }

    [Fact]
    public void HasChanges_WhenMovedKeysPresent_ReturnsTrue()
    {
        var result = new SettingsMigrationResult(["some-key"], null);

        Assert.True(result.HasChanges);
    }

    [Fact]
    public void HasChanges_WhenBackupPathPresent_ReturnsTrue()
    {
        var result = new SettingsMigrationResult([], "/tmp/settings.json.bak");

        Assert.True(result.HasChanges);
    }

    [Fact]
    public void HasChanges_WhenNeitherPresent_ReturnsFalse()
    {
        var result = new SettingsMigrationResult([], null);

        Assert.False(result.HasChanges);
    }
}
