using System;
using System.Globalization;
using Avalonia.Data.Converters;

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
}
