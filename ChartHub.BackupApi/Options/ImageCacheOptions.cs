namespace ChartHub.BackupApi.Options;

public sealed class ImageCacheOptions
{
    public const string SectionName = "ImageCache";

    public string CacheDirectory { get; set; } = "./cache/images";
}