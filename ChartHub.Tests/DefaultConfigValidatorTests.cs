using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Configuration.Stores;
using ChartHub.Tests.TestInfrastructure;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.IntegrationLite)]
public class DefaultConfigValidatorTests
{
    [Fact]
    public void Validate_WhenRequiredPathBlank_ReturnsFailure()
    {
        AppConfigRoot config = CreateConfigTemplate();
        config.Runtime.TempDirectory = "   ";

        var sut = new DefaultConfigValidator();
        ConfigValidationResult result = sut.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Key == "Runtime.TempDirectory");
    }

    [Fact]
    public void Validate_WhenPathIsNonFileUri_ReturnsFailure()
    {
        AppConfigRoot config = CreateConfigTemplate();
        config.Runtime.DownloadDirectory = "https://example.com/downloads";

        var sut = new DefaultConfigValidator();
        ConfigValidationResult result = sut.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Key == "Runtime.DownloadDirectory");
    }

    [Fact]
    public void Validate_WhenPathPointsToFile_ReturnsFailure()
    {
        using var temp = new TemporaryDirectoryFixture("validator-file-path");
        string filePath = temp.GetPath("not-a-directory.txt");
        File.WriteAllText(filePath, "x");

        AppConfigRoot config = CreateConfigTemplate();
        config.Runtime.StagingDirectory = filePath;

        var sut = new DefaultConfigValidator();
        ConfigValidationResult result = sut.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Key == "Runtime.StagingDirectory");
    }

    [Fact]
    public void Validate_WhenDirectoryExists_ReturnsSuccess()
    {
        using var temp = new TemporaryDirectoryFixture("validator-existing-dir");

        AppConfigRoot config = CreateConfigTemplate();
        config.Runtime.TempDirectory = temp.GetPath("temp");
        config.Runtime.DownloadDirectory = temp.GetPath("downloads");
        config.Runtime.StagingDirectory = temp.GetPath("staging");
        config.Runtime.OutputDirectory = temp.GetPath("output");
        config.Runtime.CloneHeroDataDirectory = temp.GetPath("clonehero");
        config.Runtime.CloneHeroSongDirectory = temp.GetPath("clonehero/songs");

        Directory.CreateDirectory(config.Runtime.TempDirectory);
        Directory.CreateDirectory(config.Runtime.DownloadDirectory);
        Directory.CreateDirectory(config.Runtime.StagingDirectory);
        Directory.CreateDirectory(config.Runtime.OutputDirectory);
        Directory.CreateDirectory(config.Runtime.CloneHeroDataDirectory);
        Directory.CreateDirectory(config.Runtime.CloneHeroSongDirectory);

        var sut = new DefaultConfigValidator();
        ConfigValidationResult result = sut.Validate(config);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenParentDirectoryExists_ReturnsSuccess()
    {
        using var temp = new TemporaryDirectoryFixture("validator-parent-dir");
        string parent = temp.GetPath("existing-parent");
        Directory.CreateDirectory(parent);

        AppConfigRoot config = CreateConfigTemplate();
        config.Runtime.TempDirectory = Path.Combine(parent, "child-temp");
        config.Runtime.DownloadDirectory = Path.Combine(parent, "child-download");
        config.Runtime.StagingDirectory = Path.Combine(parent, "child-stage");
        config.Runtime.OutputDirectory = Path.Combine(parent, "child-output");
        config.Runtime.CloneHeroDataDirectory = Path.Combine(parent, "child-data");
        config.Runtime.CloneHeroSongDirectory = Path.Combine(parent, "child-songs");

        var sut = new DefaultConfigValidator();
        ConfigValidationResult result = sut.Validate(config);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenTransferConcurrencyCapBelowMinimum_ReturnsFailure()
    {
        AppConfigRoot config = CreateConfigTemplate();
        config.Runtime.TransferOrchestratorConcurrencyCap = 0;

        var sut = new DefaultConfigValidator();
        ConfigValidationResult result = sut.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Key == "Runtime.TransferOrchestratorConcurrencyCap");
    }

    [Fact]
    public void Validate_WhenTransferConcurrencyCapAboveMaximum_ReturnsFailure()
    {
        AppConfigRoot config = CreateConfigTemplate();
        config.Runtime.TransferOrchestratorConcurrencyCap = 99;

        var sut = new DefaultConfigValidator();
        ConfigValidationResult result = sut.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Key == "Runtime.TransferOrchestratorConcurrencyCap");
    }

    [Fact]
    public void Validate_WhenDesktopBaseUrlInvalid_ReturnsFailure()
    {
        AppConfigRoot config = CreateConfigTemplate();
        config.Runtime.SyncApiDesktopBaseUrl = "not a url";

        var sut = new DefaultConfigValidator();
        ConfigValidationResult result = sut.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Key == "Runtime.SyncApiDesktopBaseUrl");
    }

    [Fact]
    public void Validate_WhenListenPrefixMissingTrailingSlash_ReturnsFailure()
    {
        AppConfigRoot config = CreateConfigTemplate();
        config.Runtime.SyncApiListenPrefix = "http://127.0.0.1:15123";

        var sut = new DefaultConfigValidator();
        ConfigValidationResult result = sut.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Key == "Runtime.SyncApiListenPrefix");
    }

    [Fact]
    public void Validate_WhenAdvertisedBaseUrlInvalid_ReturnsFailure()
    {
        AppConfigRoot config = CreateConfigTemplate();
        config.Runtime.SyncApiAdvertisedBaseUrl = "not a url";

        var sut = new DefaultConfigValidator();
        ConfigValidationResult result = sut.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Key == "Runtime.SyncApiAdvertisedBaseUrl");
    }

    [Fact]
    public void Validate_WhenAdvertisedBaseUrlPortDiffersFromListenPrefix_ReturnsFailure()
    {
        AppConfigRoot config = CreateConfigTemplate();
        config.Runtime.SyncApiListenPrefix = "http://0.0.0.0:15123/";
        config.Runtime.SyncApiAdvertisedBaseUrl = "http://192.168.1.55:16123";

        var sut = new DefaultConfigValidator();
        ConfigValidationResult result = sut.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Key == "Runtime.SyncApiAdvertisedBaseUrl");
    }

    private static AppConfigRoot CreateConfigTemplate()
    {
        return new AppConfigRoot
        {
            Runtime = new RuntimeAppConfig
            {
                TempDirectory = "/tmp/a",
                DownloadDirectory = "/tmp/b",
                StagingDirectory = "/tmp/c",
                OutputDirectory = "/tmp/d",
                CloneHeroDataDirectory = "/tmp/e",
                CloneHeroSongDirectory = "/tmp/f",
            },
        };
    }
}
