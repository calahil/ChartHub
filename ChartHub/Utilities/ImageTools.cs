using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

using AsyncImageLoader;

using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using SkiaSharp;

using Svg.Skia;

namespace ChartHub.Utilities;

/// <summary>
/// Converter that validates image URLs and provides fallback for invalid/null values.
/// Handles runtime failures similar to FFImageLoading.
/// </summary>
public class SafeImageUrlConverter : IValueConverter
{
    private const string FallbackAlbumArt = "avares://ChartHub/Resources/Images/noalbumart.svg";
    private const string FallbackGeneric = "avares://ChartHub/Resources/Images/blank.svg";
    private const string FallbackAvatar = "avares://ChartHub/Resources/Images/blankprofile.svg";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // If URL is null, empty, or whitespace, return fallback
        if (value is not string url || string.IsNullOrWhiteSpace(url))
        {
            return GetFallback(parameter);
        }

        // If URL looks invalid, return fallback
        if (!IsValidUrl(url))
        {
            return GetFallback(parameter);
        }

        // Return the URL - AsyncImageLoader will handle the actual download
        // If it fails at runtime, AsyncImageLoader will log but not crash
        return url;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static string GetFallback(object? parameter)
    {
        // Use parameter to specify fallback type: "album", "avatar", or "generic"
        if (parameter is string param)
        {
            return param.ToLowerInvariant() switch
            {
                "album" => FallbackAlbumArt,
                "avatar" => FallbackAvatar,
                _ => FallbackGeneric,
            };
        }
        return FallbackGeneric;
    }

    private static bool IsValidUrl(string url)
    {
        // Check if it's an avares:// resource (always valid)
        if (url.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check if it's a valid HTTP(S) URL
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        return false;
    }
}

/// <summary>
/// Converts an asset path string to IImage, rasterizing SVG assets at the requested color.
/// ConverterParameter: optional hex color string (e.g. "#CAD3F5") to tint SVG icons.
/// SVG files must use fill="currentColor" as a placeholder for the substitution to work.
/// </summary>
public class AssetPathToImageConverter : IValueConverter
{
    // Default icon color — matches MacchiatoMauve from the Catppuccin Macchiato palette.
    internal const string DefaultSvgColor = "#C6A0F6";

    // Cache keyed by "uri|color" so each tinted variant is stored independently.
    internal static readonly ConcurrentDictionary<string, IImage> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string color = parameter as string ?? DefaultSvgColor;
        string cacheKey = $"{path}|{color}";

        return Cache.GetOrAdd(cacheKey, _ => Load(path, color));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    internal static IImage Load(string path, string color)
    {
        Uri uri = Uri.TryCreate(path, UriKind.Absolute, out Uri? absolute)
            ? absolute
            : path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? new Uri($"avares://ChartHub/Resources/Svg/{path}")
                : new Uri($"avares://ChartHub/Resources/Images/{path}");

        try
        {
            if (uri.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase) && !AssetLoader.Exists(uri))
            {
                return LoadSvgBitmap(new Uri("avares://ChartHub/Resources/Images/blank.svg"), color);
            }

            if (uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                return LoadSvgBitmap(uri, color);
            }

            return LoadBitmap(uri);
        }
        catch (FileNotFoundException)
        {
            return LoadSvgBitmap(new Uri("avares://ChartHub/Resources/Images/blank.svg"), color);
        }
    }

    private static Bitmap LoadBitmap(Uri uri)
    {
        using Stream stream = AssetLoader.Open(uri);
        return new Bitmap(stream);
    }

    private static Bitmap LoadSvgBitmap(Uri uri, string color)
    {
        // Read SVG text and substitute currentColor with the requested theme color.
        // This lets every SVG use fill="currentColor" as a neutral placeholder.
        using Stream stream = AssetLoader.Open(uri);
        using var reader = new StreamReader(stream);
        string svgText = reader.ReadToEnd()
            .Replace("currentColor", color, StringComparison.OrdinalIgnoreCase);

        using var svg = new SKSvg();
        using var svgStream = new MemoryStream(Encoding.UTF8.GetBytes(svgText));
        SKPicture? picture = svg.Load(svgStream);

        SKRect rect = picture?.CullRect ?? SKRect.Empty;
        int width = Math.Max(1, (int)Math.Ceiling(rect.Width));
        int height = Math.Max(1, (int)Math.Ceiling(rect.Height));

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Transparent);
        if (picture is not null)
        {
            surface.Canvas.DrawPicture(picture);
        }

        surface.Canvas.Flush();

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        return new Bitmap(ms);
    }
}

/// <summary>
/// Multi-value converter for dynamically tinting SVG icons with a bound theme color.
/// Bindings: [0] = asset path string, [1] = color hex string or SolidColorBrush.
/// Usage in XAML:
///   &lt;Image.Source&gt;
///     &lt;MultiBinding Converter="{StaticResource SvgColorImageConverter}"&gt;
///       &lt;Binding Path="MyIconPath"/&gt;
///       &lt;Binding Source="{StaticResource ThemeForegroundBrush}"/&gt;
///     &lt;/MultiBinding&gt;
///   &lt;/Image.Source&gt;
/// </summary>
public class SvgColorImageConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [string path, ..] || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string color = AssetPathToImageConverter.DefaultSvgColor;
        if (values.Count > 1)
        {
            color = values[1] switch
            {
                string hex when !string.IsNullOrWhiteSpace(hex) => hex,
                SolidColorBrush brush => $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}",
                _ => color,
            };
        }

        string cacheKey = $"{path}|{color}";
        return AssetPathToImageConverter.Cache.GetOrAdd(cacheKey, _ => AssetPathToImageConverter.Load(path, color));
    }
}
/// <summary>
/// Custom async image loader that provides fallback support for 404 and other HTTP errors.
/// Wraps the default image loader to catch download failures gracefully.
/// </summary>
public class FallbackAsyncImageLoader : IAsyncImageLoader, IDisposable
{
    private static readonly HttpClient ProbeClient = new();
    private static readonly HttpClient NetworkImageClient = CreateNetworkImageClient();
    private readonly IAsyncImageLoader _innerLoader;
    private readonly string _avatarFallback = "avares://ChartHub/Resources/Images/blankprofile.svg";
    private readonly string _albumFallback = "avares://ChartHub/Resources/Images/noalbumart.svg";
    private readonly string _genericFallback = "avares://ChartHub/Resources/Images/blank.svg";

    public FallbackAsyncImageLoader(IAsyncImageLoader? innerLoader = null)
    {
        // Use provided loader or default to built-in one
        _innerLoader = innerLoader ?? ImageLoader.AsyncImageLoader;
    }

    public async Task<Bitmap?> ProvideImageAsync(string url)
    {
        // Handle avares:// URIs directly via Avalonia's embedded asset system.
        if (url.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            return TryLoadAvaresResource(url);
        }

        // Some desktop hosts reject the default async-loader requests for plain HTTP links.
        // Try a direct image fetch first for HTTP(S) URLs.
        Bitmap? directNetworkBitmap = await TryLoadNetworkBitmapAsync(url);
        if (directNetworkBitmap is not null)
        {
            return directNetworkBitmap;
        }

        if (await IsMissingHttpResourceAsync(url))
        {
            return await TryLoadFallbackAsync(url);
        }

        try
        {
            // Attempt to load the primary URL
            return await _innerLoader.ProvideImageAsync(url);
        }
        catch (HttpRequestException ex) when (
            ex.Message.Contains("404") ||
            ex.Message.Contains("Not Found") ||
            ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // 404 Not Found - use appropriate fallback based on URL pattern
            string fallbackUrl = DetermineFallback(url);
            if (fallbackUrl == url)
            {
                // Fallback detection logic couldn't determine a fallback, return null
                return null;
            }

            return await TryLoadFallbackAsync(url);
        }
        catch (HttpRequestException)
        {
            // For other HTTP errors (timeout, network, etc.), also use fallback
            string fallbackUrl = DetermineFallback(url);
            if (fallbackUrl == url)
            {
                return null;
            }

            return await TryLoadFallbackAsync(url);
        }
        catch
        {
            // For non-HTTP errors, propagate the exception
            throw;
        }
    }

    private static HttpClient CreateNetworkImageClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
        });

        client.DefaultRequestHeaders.UserAgent.ParseAdd("ChartHub/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        return client;
    }

    private static async Task<Bitmap?> TryLoadNetworkBitmapAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        try
        {
            using HttpResponseMessage response = await NetworkImageClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    private async Task<Bitmap?> TryLoadFallbackAsync(string originalUrl)
    {
        string fallbackUrl = DetermineFallback(originalUrl);
        if (fallbackUrl == originalUrl)
        {
            return null;
        }

        // Fallback URLs from DetermineFallback are avares:// resources — load them directly.
        if (fallbackUrl.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            return TryLoadAvaresResource(fallbackUrl);
        }

        try
        {
            return await _innerLoader.ProvideImageAsync(fallbackUrl);
        }
        catch
        {
            // If fallback also fails, return null and let Avalonia handle it
            return null;
        }
    }

    private static async Task<bool> IsMissingHttpResourceAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using HttpResponseMessage response = await ProbeClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return true;
            }

            // Some servers do not support HEAD and return 405/501. Try lightweight GET headers then.
            if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed
                || response.StatusCode == System.Net.HttpStatusCode.NotImplemented)
            {
                using var getRequest = new HttpRequestMessage(HttpMethod.Get, uri);
                using HttpResponseMessage getResponse = await ProbeClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);
                return getResponse.StatusCode == System.Net.HttpStatusCode.NotFound;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static Bitmap? TryLoadAvaresResource(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && AssetLoader.Exists(uri))
            {
                if (url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    return AssetPathToImageConverter.Load(url, AssetPathToImageConverter.DefaultSvgColor) as Bitmap;
                }

                using Stream stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private string DetermineFallback(string originalUrl)
    {
        // Detect fallback type based on URL pattern
        if (originalUrl.Contains("/cp/upload/users/") || originalUrl.Contains("avatar") || originalUrl.Contains("profile"))
        {
            return _avatarFallback;
        }

        if (originalUrl.Contains("album") || originalUrl.Contains("cover") || originalUrl.Contains("art")
            || originalUrl.Contains("files.enchor.us", StringComparison.OrdinalIgnoreCase))
        {
            return _albumFallback;
        }

        return _genericFallback;
    }

    public void Dispose()
    {
        // Dispose the inner loader if it implements IDisposable
        if (_innerLoader is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

/// <summary>
/// Negates a boolean value. Used in XAML to show/hide unrated instrument placeholders.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}
