using System.Net;

using ChartHub.BackupApi.Tests.TestInfrastructure;

namespace ChartHub.BackupApi.Tests;

/// <summary>
/// Verifies that the application starts successfully with Serilog configured and the
/// PostgreSQL log sink suppressed (SQLite provider in tests means the PG sink is never activated).
/// </summary>
[Trait(TestCategories.Category, TestCategories.IntegrationLite)]
public sealed class SerilogBootstrapTests : IClassFixture<BackupApiWebApplicationFactory>
{
    private readonly BackupApiWebApplicationFactory _factory;

    public SerilogBootstrapTests(BackupApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task App_StartsWithSerilogConfigured_HealthEndpointReturnsOk()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
