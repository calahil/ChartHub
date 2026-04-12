using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;

namespace ChartHub.Configuration.Stores;

public sealed class DefaultConfigValidator : IConfigValidator
{
    public ConfigValidationResult Validate(AppConfigRoot config)
    {
        _ = config;
        return ConfigValidationResult.Success;
    }
}
