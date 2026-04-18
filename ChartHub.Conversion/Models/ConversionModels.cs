namespace ChartHub.Conversion.Models;

/// <summary>
/// The result of converting a CON/SNG source file into a Clone Hero–compatible song folder.
/// </summary>
/// <param name="OutputDirectory">Absolute path to the produced song directory.</param>
/// <param name="Metadata">Extracted song metadata.</param>
public sealed record ConversionResult(string OutputDirectory, ConversionMetadata Metadata);

/// <summary>Song metadata extracted from the source file.</summary>
/// <param name="Artist">Song artist.</param>
/// <param name="Title">Song title.</param>
/// <param name="Charter">Chart author.</param>
public sealed record ConversionMetadata(string Artist, string Title, string Charter);

/// <summary>Options controlling conversion behaviour.</summary>
public sealed class ConversionOptions
{
    /// <summary>
    /// Path to the ffmpeg executable used for MOGG audio decoding.
    /// When <c>null</c>, "ffmpeg" is resolved from PATH.
    /// </summary>
    public string? FfmpegPath { get; init; }
}
