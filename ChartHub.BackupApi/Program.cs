using ChartHub.BackupApi.Endpoints;
using ChartHub.BackupApi.Models;
using ChartHub.BackupApi.Options;
using ChartHub.BackupApi.Persistence;
using ChartHub.BackupApi.Services;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<RhythmVerseSourceOptions>(builder.Configuration.GetSection(RhythmVerseSourceOptions.SectionName));
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
builder.Services.Configure<DownloadOptions>(builder.Configuration.GetSection(DownloadOptions.SectionName));
builder.Services.Configure<ImageCacheOptions>(builder.Configuration.GetSection(ImageCacheOptions.SectionName));

builder.Services.AddDbContext<BackupDbContext>((serviceProvider, options) =>
{
    IConfiguration configuration = serviceProvider.GetRequiredService<IConfiguration>();
    string provider = configuration[$"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.Provider)}"]
        ?? new DatabaseOptions().Provider;
    string sqliteConnectionString = configuration[$"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.SqliteConnectionString)}"]
        ?? new DatabaseOptions().SqliteConnectionString;
    string postgreSqlConnectionString = configuration[$"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.PostgreSqlConnectionString)}"]
        ?? new DatabaseOptions().PostgreSqlConnectionString;

    if (string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(sqliteConnectionString);
    }
    else
    {
        options.UseNpgsql(postgreSqlConnectionString);
    }
});

builder.Services.AddHttpClient<IRhythmVerseUpstreamClient, RhythmVerseUpstreamClient>();
builder.Services.AddHttpClient<IImageProxyService, ImageProxyService>();
builder.Services.AddScoped<IRhythmVerseRepository, RhythmVerseRepository>();
builder.Services.AddSingleton<ISchemaDocumentService, SchemaDocumentService>();

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<RhythmVerseSyncBackgroundService>();
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

WebApplication app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    BackupDbContext dbContext = scope.ServiceProvider.GetRequiredService<BackupDbContext>();

    if (!app.Environment.IsEnvironment("Testing"))
    {
        // Apply any pending migrations on startup.
        // New migrations are generated via: dotnet ef migrations add <Name> --project ChartHub.BackupApi
        dbContext.Database.Migrate();
    }
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        Exception? exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

        IResult result = exception switch
        {
            StoredSongPayloadException => Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Stored RhythmVerse payload is invalid",
                detail: "The requested RhythmVerse song data could not be read because the stored payload is invalid."),
            _ => Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected server error occurred.")
        };

        await result.ExecuteAsync(context);
    });
});

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("GetBackupApiHealth")
    .WithTags("System")
    .WithSummary("Get service health status")
    .WithDescription("Simple unauthenticated liveness endpoint for service and infrastructure health checks.")
    .Produces<BackupApiHealthResponse>(StatusCodes.Status200OK);
app.MapRhythmVerseEndpoints();

app.Run();

public partial class Program;
