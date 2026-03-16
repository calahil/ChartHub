using RhythmVerseClient.Configuration.Models;
using RhythmVerseClient.Configuration.Stores;
using RhythmVerseClient.Tests.TestInfrastructure;

namespace RhythmVerseClient.Tests;

[Trait(RhythmVerseClient.Tests.TestInfrastructure.TestCategories.Category, RhythmVerseClient.Tests.TestInfrastructure.TestCategories.IntegrationLite)]
public class DefaultConfigValidatorTests
{
    [Fact]
    public void Validate_WhenRequiredPathBlank_ReturnsFailure()
    {
        var config = CreateConfigTemplate();
        config.Runtime.TempDirectory = "   ";

        var sut = new DefaultConfigValidator();
        var result = sut.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Key == "Runtime.TempDirectory");
    }

    [Fact]
    public void Validate_WhenPathIsNonFileUri_ReturnsFailure()
    {
        var config = CreateConfigTemplate();
        config.Runtime.DownloadDirectory = "https://example.com/downloads";

        var sut = new DefaultConfigValidator();
        var result = sut.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Key == "Runtime.DownloadDirectory");
    }

    [Fact]
    public void Validate_WhenPathPointsToFile_ReturnsFailure()
    {
        using var temp = new TemporaryDirectoryFixture("validator-file-path");
        var filePath = temp.GetPath("not-a-directory.txt");
        File.WriteAllText(filePath, "x");

        var config = CreateConfigTemplate();
        config.Runtime.StagingDirectory = filePath;

        var sut = new DefaultConfigValidator();
        var result = sut.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, failure => failure.Key == "Runtime.StagingDirectory");
    }

    [Fact]
    public void Validate_WhenDirectoryExists_ReturnsSuccess()
    {
        using var temp = new TemporaryDirectoryFixture("validator-existing-dir");

        var config = CreateConfigTemplate();
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
        var result = sut.Validate(config);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenParentDirectoryExists_ReturnsSuccess()
    {
        using var temp = new TemporaryDirectoryFixture("validator-parent-dir");
        var parent = temp.GetPath("existing-parent");
        Directory.CreateDirectory(parent);

        var config = CreateConfigTemplate();
        config.Runtime.TempDirectory = Path.Combine(parent, "child-temp");
        config.Runtime.DownloadDirectory = Path.Combine(parent, "child-download");
        config.Runtime.StagingDirectory = Path.Combine(parent, "child-stage");
        config.Runtime.OutputDirectory = Path.Combine(parent, "child-output");
        config.Runtime.CloneHeroDataDirectory = Path.Combine(parent, "child-data");
        config.Runtime.CloneHeroSongDirectory = Path.Combine(parent, "child-songs");

        var sut = new DefaultConfigValidator();
        var result = sut.Validate(config);

        Assert.True(result.IsValid);
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
