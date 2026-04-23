using ChartHub.Conversion;
using ChartHub.Conversion.Models;

namespace ChartHub.Conversion.Tests.Parity;

internal static class ChartHubParityRunner
{
    internal static async Task<string> EnsureChartHubOutputAsync(string inputPath, string outputPath)
    {
        if (Directory.Exists(outputPath))
        {
            Directory.Delete(outputPath, recursive: true);
        }

        Directory.CreateDirectory(outputPath);

        var conversionService = new ConversionService();
        ConversionResult result = await conversionService
            .ConvertAsync(inputPath, outputPath)
            .ConfigureAwait(false);

        if (!Directory.Exists(result.OutputDirectory))
        {
            throw new InvalidOperationException($"ChartHub conversion did not produce output directory '{result.OutputDirectory}'.");
        }

        // Conversion may return a parent folder containing a single song folder.
        string[] childDirectories = Directory.GetDirectories(result.OutputDirectory);
        string[] childFiles = Directory.GetFiles(result.OutputDirectory);
        if (childFiles.Length == 0 && childDirectories.Length == 1)
        {
            return childDirectories[0];
        }

        return result.OutputDirectory;
    }
}
