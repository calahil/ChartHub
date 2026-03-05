# .NET 10.0 Upgrade Plan

## Execution Steps

Execute steps below sequentially one by one in the order they are listed.

1. Validate that .NET 10 SDK required for this upgrade is installed on the machine and if not, help to get it installed.
2. Upgrade RhythmVerseClient/RhythmVerseClient.csproj to .NET 10.0
3. Upgrade SettingsManager/SettingsManager.csproj to .NET 10.0
4. Run unit tests to validate upgrade in the projects.

## Settings

### Aggregate NuGet packages modifications across all projects

NuGet packages used across all selected projects that need version update for .NET 10.0 compatibility.

| Package Name                                | Current Version | New Version | Description                                   |
|:-------------------------------------------|:---------------:|:-----------:|:----------------------------------------------|
| CommunityToolkit.Maui                      |     9.0.2       |   10.0.0    | Update for .NET 10.0 compatibility            |
| CommunityToolkit.Mvvm                      |     8.2.2       |   8.3.1     | Update for .NET 10.0 compatibility            |
| FFImageLoading.Maui                        |     1.2.6       |   1.2.9     | Update for .NET 10.0 compatibility            |
| Google.Apis.Drive.v3                       |   1.68.0.3466   | 1.68.0.3470 | Update for .NET 10.0 compatibility            |
| HtmlAgilityPack                            |    1.11.62      |   1.11.65   | Update for .NET 10.0 compatibility            |
| Microsoft.Extensions.Configuration.UserSecrets |   8.0.0      |   10.0.0    | Update for .NET 10.0 compatibility            |
| Microsoft.Extensions.Logging.Debug         |     8.0.0       |   10.0.0    | Update for .NET 10.0 compatibility            |
| Microsoft.Maui.Controls                    |    8.0.71       |   10.0.1    | Update for .NET 10.0 compatibility            |
| Microsoft.Maui.Controls.Compatibility      |    8.0.71       |   10.0.1    | Update for .NET 10.0 compatibility            |
| SharpCompress                              |     0.37.2      |   0.37.5    | Update for .NET 10.0 compatibility            |
| Syncfusion.Maui.DataGrid                   |    26.2.7       |   28.1.33   | Update for .NET 10.0 compatibility            |
| System.Text.Json                           |     8.0.4       |   10.0.0    | Update for .NET 10.0 compatibility            |
| WinUIEx                                    |     1.8.0       |   2.4.1     | Update for .NET 10.0 compatibility            |

### Project upgrade details

#### RhythmVerseClient/RhythmVerseClient.csproj modifications

Project properties changes:
- Target frameworks should be changed from `net8.0` and `net8.0-windows10.0.19041.0` to `net10.0` and `net10.0-windows10.0.19041.0`

NuGet packages changes:
- All packages listed in the "Aggregate NuGet packages modifications across all projects" section need to be updated to their new versions.

#### SettingsManager/SettingsManager.csproj modifications

Project properties changes:
- Target frameworks should be changed from `net8.0` and `net8.0-windows10.0.19041.0` to `net10.0` and `net10.0-windows10.0.19041.0`

NuGet packages changes:
- Microsoft.Maui.Controls should be updated from `$(MauiVersion)` to ensure compatibility with .NET 10.0
- Microsoft.Maui.Controls.Compatibility should be updated from `$(MauiVersion)` to ensure compatibility with .NET 10.0
