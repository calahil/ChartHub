using System;
using System.Collections.Concurrent;
using System.Globalization;
using AsyncImageLoader;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using Svg.Skia;

namespace RhythmVerseClient.Utilities
{
    /// <summary>
    /// Converter that validates image URLs and provides fallback for invalid/null values.
    /// Handles runtime failures similar to FFImageLoading.
    /// </summary>
    public class SafeImageUrlConverter : IValueConverter
    {
        private const string FallbackAlbumArt = "avares://RhythmVerseClient/Resources/Images/noalbumart.png";
        private const string FallbackGeneric = "avares://RhythmVerseClient/Resources/Images/blank.png";
        private const string FallbackAvatar = "avares://RhythmVerseClient/Resources/Images/blankprofile.png";

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
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
            }

            return false;
        }
    }

      /// <summary>
    /// Converter that append asset paths.
    /// </summary>
    public class AssetPathToImageConverter : IValueConverter
    {
        private static readonly ConcurrentDictionary<string, IImage> Cache = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return Cache.GetOrAdd(path, static p =>
            {
                var uri = Uri.TryCreate(p, UriKind.Absolute, out var absoluteUri)
                    ? absoluteUri
                    : p.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                        ? new Uri($"avares://RhythmVerseClient/Resources/Svg/{p}")
                        : new Uri($"avares://RhythmVerseClient/Resources/Images/{p}");

                try
                {
                    if (uri.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase) && !AssetLoader.Exists(uri))
                    {
                        return LoadBitmap(new Uri("avares://RhythmVerseClient/Resources/Images/blank.png"));
                    }

                    if (uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                    {
                        return LoadSvgBitmap(uri);
                    }

                    return LoadBitmap(uri);
                }
                catch (FileNotFoundException)
                {
                    // Use a safe bundled fallback icon if a specific asset is missing.
                    return LoadBitmap(new Uri("avares://RhythmVerseClient/Resources/Images/blank.png"));
                }
            });
        }

        private static Bitmap LoadBitmap(Uri uri)
        {
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }

        private static Bitmap LoadSvgBitmap(Uri uri)
        {
            using var stream = AssetLoader.Open(uri);
            using var svg = new SKSvg();
            var picture = svg.Load(stream);

            var rect = picture?.CullRect ?? SKRect.Empty;
            var width = Math.Max(1, (int)Math.Ceiling(rect.Width));
            var height = Math.Max(1, (int)Math.Ceiling(rect.Height));

            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            surface.Canvas.Clear(SKColors.Transparent);

            if (picture is not null)
            {
                surface.Canvas.DrawPicture(picture);
            }

            surface.Canvas.Flush();

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream(data.ToArray());
            return new Bitmap(ms);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
    /// <summary>
    /// Custom async image loader that provides fallback support for 404 and other HTTP errors.
    /// Wraps the default image loader to catch download failures gracefully.
    /// </summary>
    public class FallbackAsyncImageLoader : IAsyncImageLoader, IDisposable
    {
        private static readonly HttpClient ProbeClient = new();
        private readonly IAsyncImageLoader _innerLoader;
        private readonly string _avatarFallback = "avares://RhythmVerseClient/Resources/Images/blankprofile.png";
        private readonly string _albumFallback = "avares://RhythmVerseClient/Resources/Images/noalbumart.png";
        private readonly string _genericFallback = "avares://RhythmVerseClient/Resources/Images/blank.png";

        public FallbackAsyncImageLoader(IAsyncImageLoader? innerLoader = null)
        {
            // Use provided loader or default to built-in one
            _innerLoader = innerLoader ?? ImageLoader.AsyncImageLoader;
        }

        public async Task<Bitmap?> ProvideImageAsync(string url)
        {
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
                var fallbackUrl = DetermineFallback(url);
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
                var fallbackUrl = DetermineFallback(url);
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

        private async Task<Bitmap?> TryLoadFallbackAsync(string originalUrl)
        {
            var fallbackUrl = DetermineFallback(originalUrl);
            if (fallbackUrl == originalUrl)
            {
                return null;
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
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
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
                using var response = await ProbeClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return true;
                }

                // Some servers do not support HEAD and return 405/501. Try lightweight GET headers then.
                if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed
                    || response.StatusCode == System.Net.HttpStatusCode.NotImplemented)
                {
                    using var getRequest = new HttpRequestMessage(HttpMethod.Get, uri);
                    using var getResponse = await ProbeClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);
                    return getResponse.StatusCode == System.Net.HttpStatusCode.NotFound;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private string DetermineFallback(string originalUrl)
        {
            // Detect fallback type based on URL pattern
            if (originalUrl.Contains("/cp/upload/users/") || originalUrl.Contains("avatar") || originalUrl.Contains("profile"))
            {
                return _avatarFallback;
            }
            else if (originalUrl.Contains("album") || originalUrl.Contains("cover") || originalUrl.Contains("art"))
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
}
