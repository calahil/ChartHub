using System.Text;
using System.Threading.RateLimiting;

using ChartHub.Server.Endpoints;
using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
builder.Services.Configure<GoogleDriveOptions>(builder.Configuration.GetSection(GoogleDriveOptions.SectionName));
builder.Services.Configure<ServerPathOptions>(builder.Configuration.GetSection(ServerPathOptions.SectionName));
builder.Services.Configure<DownloadsOptions>(builder.Configuration.GetSection(DownloadsOptions.SectionName));
builder.Services.Configure<DesktopEntryOptions>(builder.Configuration.GetSection(DesktopEntryOptions.SectionName));
builder.Services.Configure<ServerLoggingOptions>(builder.Configuration.GetSection(ServerLoggingOptions.SectionName));
builder.Services.Configure<InputOptions>(builder.Configuration.GetSection(InputOptions.SectionName));
builder.Services.Configure<HudOptions>(builder.Configuration.GetSection(HudOptions.SectionName));

ServerLoggingOptions serverLoggingOptions = builder.Configuration
    .GetSection(ServerLoggingOptions.SectionName)
    .Get<ServerLoggingOptions>() ?? new ServerLoggingOptions();

string serverLogDirectory = ServerContentPathResolver.Resolve(serverLoggingOptions.LogDirectory, builder.Environment.ContentRootPath);
builder.Logging.AddProvider(new ServerFileLoggerProvider(serverLogDirectory, serverLoggingOptions.FileName));

ServerPathOptions serverPathOptions = builder.Configuration
    .GetSection(ServerPathOptions.SectionName)
    .Get<ServerPathOptions>() ?? new ServerPathOptions();

string sqliteDbPath = serverPathOptions.SqliteDbPath;
if (!Path.IsPathRooted(sqliteDbPath))
{
    sqliteDbPath = Path.Combine(builder.Environment.ContentRootPath, sqliteDbPath);
}

builder.Logging.AddProvider(new SqliteServerEventLoggerProvider(sqliteDbPath));

AuthOptions authOptions = builder.Configuration
    .GetSection(AuthOptions.SectionName)
    .Get<AuthOptions>() ?? new AuthOptions();

if (authOptions.JwtSigningKey.Length < 32)
{
    throw new InvalidOperationException("Auth:JwtSigningKey must be at least 32 characters.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = authOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.JwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddSingleton<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
builder.Services.AddSingleton<IJwtTokenIssuer, JwtTokenIssuer>();
builder.Services.AddHttpClient("downloads");
builder.Services.AddSingleton<ISourceUrlResolver, SourceUrlResolver>();
builder.Services.AddSingleton<IGoogleDriveFolderArchiveService, GoogleDriveFolderArchiveService>();
builder.Services.AddSingleton<IDownloadJobStore, SqliteDownloadJobStore>();
builder.Services.AddSingleton<IJobLogSink, SqliteJobLogSink>();
builder.Services.AddSingleton<IInstallConcurrencyLimiter, SemaphoreInstallConcurrencyLimiter>();
builder.Services.AddSingleton<IServerInstallFileTypeResolver, ServerInstallFileTypeResolver>();
builder.Services.AddSingleton<IServerSongIniMetadataParser, ServerSongIniMetadataParser>();
builder.Services.AddSingleton<IServerCloneHeroDirectorySchemaService, ServerCloneHeroDirectorySchemaService>();
builder.Services.AddSingleton<IServerOnyxInstallService, ServerOnyxInstallService>();
builder.Services.AddSingleton<IDownloadJobInstallService, DownloadJobInstallService>();
builder.Services.AddSingleton<ICloneHeroLibraryService, CloneHeroLibraryService>();
builder.Services.AddSingleton<IDesktopEntryService, DesktopEntryService>();
builder.Services.AddSingleton<IVolumeService, VolumeService>();
builder.Services.AddSingleton<IInputConnectionTracker, InputConnectionTracker>();
builder.Services.AddSingleton<HudLifecycleService>();
builder.Services.AddSingleton<IHudLifecycleService>(sp => sp.GetRequiredService<HudLifecycleService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<HudLifecycleService>());
builder.Services.AddHostedService<ServerPathValidatorHostedService>();
builder.Services.AddHostedService<NestedInstallPathMigrationService>();
builder.Services.AddHostedService<DownloadPipelineHostedService>();
builder.Services.AddHostedService<DesktopEntryStartupHostedService>();
builder.Services.AddSingleton<IUinputGamepadService, UinputGamepadService>();
builder.Services.AddSingleton<IUinputMouseService, UinputMouseService>();
builder.Services.AddSingleton<IUinputKeyboardService, UinputKeyboardService>();

builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", context =>
        RateLimitPartition.GetTokenBucketLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 10,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                TokensPerPeriod = 10,
                AutoReplenishment = true,
            }));

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        string clientIp = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        return RateLimitPartition.GetTokenBucketLimiter(clientIp, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 120,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            TokensPerPeriod = 120,
            AutoReplenishment = true,
        });
    });
});

WebApplication app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        Exception? exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

        await Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected server error occurred.")
            .ExecuteAsync(context)
            .ConfigureAwait(false);
    });
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapScalarApiReference();
}

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("GetChartHubServerHealth")
    .WithTags("System");

app.MapAuthEndpoints();
app.MapDownloadEndpoints();
app.MapCloneHeroEndpoints();
app.MapDesktopEntryEndpoints();
app.MapVolumeEndpoints();
app.MapInputEndpoints();
app.MapHudStatusEndpoints();

app.Run();

public partial class Program;
