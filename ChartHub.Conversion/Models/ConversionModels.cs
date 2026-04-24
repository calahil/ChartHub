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

/// <summary>
/// Progress update emitted by the conversion pipeline.
/// </summary>
/// <param name="Stage">Machine-readable stage identifier (for example <c>Converting:ParseDta</c>).</param>
/// <param name="ProgressPercent">Overall job progress percentage reported to the server pipeline.</param>
/// <param name="Message">Optional human-readable detail for diagnostics.</param>
public sealed record ConversionProgressUpdate(string Stage, double ProgressPercent, string? Message = null);

/// <summary>Known conversion stage names used for server/UI progress reporting.</summary>
public static class ConversionProgressStages
{
    public const string ParseContainer = "Converting:ParseContainer";
    public const string ParseDta = "Converting:ParseDta";
    public const string ConvertMidi = "Converting:ConvertMidi";
    public const string DecodeMogg = "Converting:DecodeMogg";
    public const string MixBacking = "Converting:MixBacking";
    public const string MixStems = "Converting:MixStems";
    public const string ExtractAlbumArt = "Converting:ExtractAlbumArt";
    public const string WriteSongIni = "Converting:WriteSongIni";
    public const string Finalize = "Converting:Finalize";
}

/// <summary>Options controlling conversion behaviour.</summary>
public sealed class ConversionOptions
{
}
