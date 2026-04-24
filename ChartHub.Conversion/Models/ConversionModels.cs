namespace ChartHub.Conversion.Models;

/// <summary>
/// The result of converting a CON/SNG source file into a Clone Hero–compatible song folder.
/// </summary>
/// <param name="OutputDirectory">Absolute path to the produced song directory.</param>
/// <param name="Metadata">Extracted song metadata.</param>
/// <param name="Statuses">Optional conversion status entries describing degraded-but-successful outcomes.</param>
public sealed record ConversionResult(
    string OutputDirectory,
    ConversionMetadata Metadata,
    IReadOnlyList<ConversionStatus>? Statuses = null);

/// <summary>Known conversion status codes emitted in <see cref="ConversionResult.Statuses"/>.</summary>
public static class ConversionStatusCodes
{
    /// <summary>
    /// Conversion succeeded, but only backing audio was produced (instrument stems were unavailable).
    /// </summary>
    public const string AudioIncomplete = "audio-incomplete";
}

/// <summary>
/// A non-fatal conversion status entry that callers can surface for diagnostics.
/// </summary>
/// <param name="Code">Stable machine-readable status code.</param>
/// <param name="Message">Human-readable status message.</param>
public sealed record ConversionStatus(string Code, string Message);

/// <summary>Song metadata extracted from the source file.</summary>
/// <param name="Artist">Song artist.</param>
/// <param name="Title">Song title.</param>
/// <param name="Charter">Chart author.</param>
public sealed record ConversionMetadata(string Artist, string Title, string Charter);

/// <summary>Options controlling conversion behaviour.</summary>
public sealed class ConversionOptions
{
}
