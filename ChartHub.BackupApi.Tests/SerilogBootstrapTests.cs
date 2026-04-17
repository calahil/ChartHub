using System.Net;

using ChartHub.BackupApi.Tests.TestInfrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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
        using HttpClient client = _factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void App_WhenPostgreSqlProviderAndConnectionStringEmpty_ThrowsOnStartup()
    {
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(
                    [
                        new KeyValuePair<string, string?>("Database:Provider", "postgresql"),
                        new KeyValuePair<string, string?>("Database:PostgreSqlConnectionString", ""),
                    ]);
                });
            });

        Exception ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        // ValidateOnStart wraps OptionsValidationException; unwrap to find it.
        while (ex.InnerException != null
               && ex.Message.Contains("PostgreSqlConnectionString", StringComparison.Ordinal) == false)
        {
            ex = ex.InnerException;
        }

        Assert.Contains("PostgreSqlConnectionString", ex.Message, StringComparison.Ordinal);
    }
}
