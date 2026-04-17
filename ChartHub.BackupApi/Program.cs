using System.Threading.RateLimiting;

using ChartHub.BackupApi.Endpoints;
using ChartHub.BackupApi.Middleware;
using ChartHub.BackupApi.Models;
using ChartHub.BackupApi.Options;
using ChartHub.BackupApi.Persistence;
using ChartHub.BackupApi.Services;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

using NpgsqlTypes;

using Serilog;
using Serilog.Events;
using Serilog.Sinks.PostgreSQL.ColumnWriters;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection(ApiKeyOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<RhythmVerseSourceOptions>(builder.Configuration.GetSection(RhythmVerseSourceOptions.SectionName));
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
builder.Services.Configure<DownloadOptions>(builder.Configuration.GetSection(DownloadOptions.SectionName));
builder.Services.Configure<ImageCacheOptions>(builder.Configuration.GetSection(ImageCacheOptions.SectionName));
builder.Services.Configure<LoggingOptions>(builder.Configuration.GetSection(LoggingOptions.SectionName));

// Fail fast: API key must be configured before the app starts.
string apiKey = builder.Configuration[$"{ApiKeyOptions.SectionName}:{nameof(ApiKeyOptions.Key)}"] ?? string.Empty;
if (!builder.Environment.IsEnvironment("Testing") && string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("ApiKey:Key must be set. Configure it via the ApiKey__Key environment variable.");
}

// Read DB provider and logging options early for Serilog sink configuration.
string dbProvider = builder.Configuration[$"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.Provider)}"]
    ?? new DatabaseOptions().Provider;
string pgConnectionString = builder.Configuration[$"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.PostgreSqlConnectionString)}"]
    ?? new DatabaseOptions().PostgreSqlConnectionString;

LoggingOptions loggingOptions = builder.Configuration
    .GetSection(LoggingOptions.SectionName)
    .Get<LoggingOptions>() ?? new LoggingOptions();

builder.Host.UseSerilog((ctx, services, config) =>
{
    // Minimum levels and category overrides are driven by the Serilog section in appsettings.
    // Enrichers and sinks are declared here to remain explicit.
    config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithProperty("EnvironmentName", ctx.HostingEnvironment.EnvironmentName)
        .Enrich.WithProperty("Application", "BackupApi")
        .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information);

    // PostgreSQL sink is conditioned on provider and environment to prevent the sink
    // from attempting a connection in test runs (which use SQLite).
    if (string.Equals(dbProvider, "postgresql", StringComparison.OrdinalIgnoreCase)
        && !ctx.HostingEnvironment.IsEnvironment("Testing"))
    {
        IDictionary<string, ColumnWriterBase> columnOptions = new Dictionary<string, ColumnWriterBase>
        {
            { "message", new RenderedMessageColumnWriter(NpgsqlDbType.Text) },
            { "message_template", new MessageTemplateColumnWriter(NpgsqlDbType.Text) },
            { "level", new LevelColumnWriter(true, NpgsqlDbType.Varchar) },
            { "raise_date", new TimestampColumnWriter(NpgsqlDbType.TimestampTz) },
            { "exception", new ExceptionColumnWriter(NpgsqlDbType.Text) },
            { "properties", new LogEventSerializedColumnWriter(NpgsqlDbType.Jsonb) },
        };

        config.WriteTo.PostgreSQL(
            connectionString: pgConnectionString,
            tableName: loggingOptions.SinkTableName,
            columnOptions: columnOptions,
            restrictedToMinimumLevel: LogEventLevel.Warning,
            needAutoCreateTable: true,
            batchSizeLimit: loggingOptions.BatchSizeLimit,
            period: TimeSpan.FromSeconds(loggingOptions.PeriodSeconds),
            retentionTime: TimeSpan.FromDays(loggingOptions.RetentionDays));
    }
});

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

builder.Services
    .AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .Validate(
        opts => !string.Equals(opts.Provider, "postgresql", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(opts.PostgreSqlConnectionString),
        $"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.PostgreSqlConnectionString)} must be set when using the postgresql provider.")
    .ValidateOnStart();

builder.Services.AddSingleton<IDnsResolver, SystemDnsResolver>();
builder.Services.AddHttpClient<IRhythmVerseUpstreamClient, RhythmVerseUpstreamClient>();
builder.Services.AddHttpClient<IImageProxyService, ImageProxyService>();
builder.Services.AddHttpClient<IDownloadProxyService, DownloadProxyService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<IRhythmVerseRepository, RhythmVerseRepository>();
builder.Services.AddSingleton<ISchemaDocumentService, SchemaDocumentService>();

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<RhythmVerseSyncBackgroundService>();
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            string clientIp = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown";
            return RateLimitPartition.GetTokenBucketLimiter(clientIp, _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 60,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                TokensPerPeriod = 60,
                AutoReplenishment = true,
            });
        });
    });
}

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

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseRateLimiter();
}

app.UseMiddleware<ApiKeyMiddleware>();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next(context);
});

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

// Request logging sits after UseExceptionHandler so the final status code (including
// 500s rewritten by exception handling) is captured in the structured log entry.
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? string.Empty);
        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
    };
});

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("GetBackupApiHealth")
    .WithTags("System")
    .WithSummary("Get service health status")
    .WithDescription("Simple unauthenticated liveness endpoint for service and infrastructure health checks.")
    .Produces<BackupApiHealthResponse>(StatusCodes.Status200OK);
app.MapRhythmVerseEndpoints();

app.Run();

public partial class Program;
