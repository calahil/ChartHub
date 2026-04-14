using System;
using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ChartHub.Utilities;

/// <summary>
/// Converts a file path or URL string to an IImage by mapping the file extension to
/// a themed SVG icon from Resources/Images. Used by SharedDownloadCardsView where
/// only a path/URL is available.
/// </summary>
public class DownloadFileTypeIconConverter : IValueConverter
{
    private const string RarIcon = "avares://ChartHub/Resources/Images/rar.svg";
    private const string ZipIcon = "avares://ChartHub/Resources/Images/zip.svg";
    private const string SevenZipIcon = "avares://ChartHub/Resources/Images/sevenzip.svg";
    private const string BinIcon = "avares://ChartHub/Resources/Images/bin.svg";
    private const string BlankIcon = "avares://ChartHub/Resources/Images/blank.svg";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? path = value as string;
        string iconPath = ResolveIconPath(path);
        return AssetPathToImageConverter.Cache.GetOrAdd(
            $"{iconPath}|{AssetPathToImageConverter.DefaultSvgColor}",
            _ => AssetPathToImageConverter.Load(iconPath, AssetPathToImageConverter.DefaultSvgColor));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    internal static string ResolveIconPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BlankIcon;
        }

        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".rar" => RarIcon,
            ".zip" => ZipIcon,
            ".7z" => SevenZipIcon,
            ".con" or ".rb3con" => BinIcon,
            _ => BlankIcon,
        };
    }
}

/// <summary>
/// Converts a ServerInstallFileType string value ("Zip", "Rar", "SevenZip", "Con", "Unknown")
/// to an IImage. Used by DownloadView where the server has already resolved the file type.
/// </summary>
public class DownloadFileTypeToIconConverter : IValueConverter
{
    private const string RarIcon = "avares://ChartHub/Resources/Images/rar.svg";
    private const string ZipIcon = "avares://ChartHub/Resources/Images/zip.svg";
    private const string SevenZipIcon = "avares://ChartHub/Resources/Images/sevenzip.svg";
    private const string BinIcon = "avares://ChartHub/Resources/Images/bin.svg";
    private const string BlankIcon = "avares://ChartHub/Resources/Images/blank.svg";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string iconPath = ResolveIconPath(value as string);
        return AssetPathToImageConverter.Cache.GetOrAdd(
            $"{iconPath}|{AssetPathToImageConverter.DefaultSvgColor}",
            _ => AssetPathToImageConverter.Load(iconPath, AssetPathToImageConverter.DefaultSvgColor));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static string ResolveIconPath(string? fileType) =>
        fileType switch
        {
            "Zip" => ZipIcon,
            "Rar" => RarIcon,
            "SevenZip" => SevenZipIcon,
            "Con" => BinIcon,
            _ => BlankIcon,
        };
}
