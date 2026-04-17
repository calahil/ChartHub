using System.Net.Http.Json;
using System.Text.Json;

using ChartHub.BackupApi.Tests.TestInfrastructure;

namespace ChartHub.BackupApi.Tests;

[Trait(TestCategories.Category, TestCategories.IntegrationLite)]
public sealed class SyncHealthEndpointTests : IClassFixture<BackupApiWebApplicationFactory>
{
    private readonly BackupApiWebApplicationFactory _factory;

    public SyncHealthEndpointTests(BackupApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetSyncHealthAsync_WithCompletedRun_ReturnsReconciliationFields()
    {
        string startedUtc = new DateTimeOffset(2026, 03, 24, 10, 0, 0, TimeSpan.Zero).ToString("O");
        string completedUtc = new DateTimeOffset(2026, 03, 24, 10, 15, 0, TimeSpan.Zero).ToString("O");
        string lastSuccessUtc = new DateTimeOffset(2026, 03, 24, 10, 15, 0, TimeSpan.Zero).ToString("O");

        await _factory.SeedSyncStatesAsync(
        [
            new("sync.last_success_utc", lastSuccessUtc),
            new("records.total_available", "154798"),
            new("sync.last_record_updated", "1772421663"),
            new("reconciliation.current_run_id", "run-123"),
            new("reconciliation.started_utc", startedUtc),
            new("reconciliation.completed_utc", completedUtc),
        ]);

        HttpClient client = _factory.CreateAuthenticatedClient();
        JsonElement response = await client.GetFromJsonAsync<JsonElement>("/api/rhythmverse/health/sync");

        Assert.Equal("run-123", response.GetProperty("reconciliation_current_run_id").GetString());
        Assert.Equal(startedUtc, response.GetProperty("reconciliation_started_utc").GetString());
        Assert.Equal(completedUtc, response.GetProperty("reconciliation_completed_utc").GetString());
        Assert.False(response.GetProperty("reconciliation_in_progress").GetBoolean());
        Assert.True(response.GetProperty("last_run_completed").GetBoolean());
        Assert.Equal("154798", response.GetProperty("total_available").GetString());
        Assert.Equal("1772421663", response.GetProperty("last_record_updated_unix").GetString());
        Assert.Equal(lastSuccessUtc, response.GetProperty("last_success_utc").GetString());
    }

    [Fact]
    public async Task GetSyncHealthAsync_WithIncompleteRun_ReturnsInProgressTrue()
    {
        string startedUtc = new DateTimeOffset(2026, 03, 25, 8, 0, 0, TimeSpan.Zero).ToString("O");

        await _factory.SeedSyncStatesAsync(
        [
            new("reconciliation.current_run_id", "run-incomplete"),
            new("reconciliation.started_utc", startedUtc),
        ]);

        HttpClient client = _factory.CreateAuthenticatedClient();
        JsonElement response = await client.GetFromJsonAsync<JsonElement>("/api/rhythmverse/health/sync");

        Assert.Equal("run-incomplete", response.GetProperty("reconciliation_current_run_id").GetString());
        Assert.Equal(startedUtc, response.GetProperty("reconciliation_started_utc").GetString());
        Assert.True(response.GetProperty("reconciliation_in_progress").GetBoolean());
        Assert.False(response.GetProperty("last_run_completed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, response.GetProperty("reconciliation_completed_utc").ValueKind);
        Assert.Equal(JsonValueKind.Null, response.GetProperty("last_success_utc").ValueKind);
    }
}