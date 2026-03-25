using ChartHub.BackupApi.Endpoints;
using ChartHub.BackupApi.Options;
using ChartHub.BackupApi.Persistence;
using ChartHub.BackupApi.Services;

using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<RhythmVerseSourceOptions>(builder.Configuration.GetSection(RhythmVerseSourceOptions.SectionName));
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
builder.Services.Configure<DownloadOptions>(builder.Configuration.GetSection(DownloadOptions.SectionName));

DatabaseOptions databaseOptions = builder.Configuration
    .GetSection(DatabaseOptions.SectionName)
    .Get<DatabaseOptions>() ?? new DatabaseOptions();

builder.Services.AddDbContext<BackupDbContext>(options =>
{
    if (string.Equals(databaseOptions.Provider, "sqlite", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(databaseOptions.SqliteConnectionString);
    }
    else
    {
        options.UseNpgsql(databaseOptions.PostgreSqlConnectionString);
    }
});

builder.Services.AddHttpClient<IRhythmVerseUpstreamClient, RhythmVerseUpstreamClient>();
builder.Services.AddScoped<IRhythmVerseRepository, RhythmVerseRepository>();
builder.Services.AddSingleton<ISchemaDocumentService, SchemaDocumentService>();
builder.Services.AddHostedService<RhythmVerseSyncBackgroundService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

WebApplication app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    BackupDbContext dbContext = scope.ServiceProvider.GetRequiredService<BackupDbContext>();
    // Apply any pending migrations on startup.
    // New migrations are generated via: dotnet ef migrations add <Name> --project ChartHub.BackupApi
    dbContext.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapRhythmVerseEndpoints();

app.Run();
