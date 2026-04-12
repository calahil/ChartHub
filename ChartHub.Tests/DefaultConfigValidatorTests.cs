using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Configuration.Stores;
using ChartHub.Tests.TestInfrastructure;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.IntegrationLite)]
public class DefaultConfigValidatorTests
{
    [Fact]
    public void Validate_WithAnyRuntimePayload_ReturnsSuccess()
    {
        AppConfigRoot config = CreateConfigTemplate();

        var sut = new DefaultConfigValidator();
        ConfigValidationResult result = sut.Validate(config);

        Assert.True(result.IsValid);
    }

    private static AppConfigRoot CreateConfigTemplate()
    {
        return new AppConfigRoot
        {
            Runtime = new RuntimeAppConfig
            {
                ServerApiBaseUrl = "https://localhost:5001",
                ServerApiAuthToken = "token",
            },
        };
    }
}
