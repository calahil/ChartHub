using System.Security.Cryptography;
using System.Text;

using ChartHub.BackupApi.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.BackupApi.Middleware;

/// <summary>
/// Enforces API key authentication on all endpoints except <c>/health</c>.
/// The key is compared using a SHA-256 hash and <see cref="CryptographicOperations.FixedTimeEquals"/>
/// to prevent timing-based enumeration attacks.
/// </summary>
public sealed class ApiKeyMiddleware
{
    private const string HeaderName = "X-Api-Key";
    private readonly RequestDelegate _next;
    private readonly byte[] _expectedKeyHash;

    public ApiKeyMiddleware(RequestDelegate next, IOptions<ApiKeyOptions> options)
    {
        _next = next;
        _expectedKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Key));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // The /health endpoint is exempt so infrastructure probes work without credentials.
        if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        string? providedKey = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrEmpty(providedKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        byte[] providedKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));

        // Both hashes are 32 bytes, so FixedTimeEquals is always constant-time.
        if (!CryptographicOperations.FixedTimeEquals(_expectedKeyHash, providedKeyHash))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await _next(context);
    }
}
