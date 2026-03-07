using System;
using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.IO;

namespace RhythmVerseClient.Utilities
{
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
                    : new Uri($"avares://RhythmVerseClient/Resources/Images/{p}");

                try
                {
                    using var stream = AssetLoader.Open(uri);
                    return new Bitmap(stream);
                }
                catch (FileNotFoundException)
                {
                    // Use a safe bundled fallback icon if a specific asset is missing.
                    var fallbackUri = new Uri("avares://RhythmVerseClient/Resources/Images/blank.png");
                    using var fallbackStream = AssetLoader.Open(fallbackUri);
                    return new Bitmap(fallbackStream);
                }
            });
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
