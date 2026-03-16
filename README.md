# ChartHub

ChartHub is a cross-platform Avalonia client for browsing and managing custom rhythm game charts, with desktop and Android targets.

## Repository layout

- `ChartHub/`: Main app project (`net10.0` desktop and optional `net10.0-android`)
- `ChartHub.Tests/`: xUnit test suite
- `ChartHub.sln`: Solution entry point

## Requirements

- .NET SDK 10.0
- For Android builds:
	- Android SDK installed at `${HOME}/Android/Sdk`
	- Android emulator or physical Android device

## Quick start

From repository root:

```bash
dotnet build ChartHub/ChartHub.csproj
dotnet run --project ChartHub/ChartHub.csproj
```

## Run tests

```bash
dotnet test ChartHub.Tests/ChartHub.Tests.csproj
```

## Data Sources

- `RhythmVerse` and `Chorus Encore` are both available as search/download sources in the app.
- Downloads from either source can be routed through the same local destination flow.

## Library Catalog

- ChartHub stores source membership metadata in `library-catalog.db` under the app config directory.
- Source IDs are tracked per provider (`rhythmverse`, `encore`) to support `In Library` badges across views.

## Android build/install

Build Android target:

```bash
dotnet build ChartHub/ChartHub.csproj -p:EnableAndroid=true -f net10.0-android -p:AndroidSdkDirectory=$HOME/Android/Sdk
```

Install to emulator/device:

```bash
dotnet build ChartHub/ChartHub.csproj -t:Install -p:EnableAndroid=true -f net10.0-android -p:AndroidSdkDirectory=$HOME/Android/Sdk
```

You can also use the workspace tasks in `.vscode/tasks.json` for `build`, `run`, `build-android`, and emulator flows.

## Configuration

- Runtime defaults are in `ChartHub/appsettings.json`.
- Local developer secrets are loaded using user-secrets (`UserSecretsId` is set in `ChartHub/ChartHub.csproj`).
- Current API authentication key name is `rhythmverseToken` for backend compatibility.

## Notes

- The app name has been migrated to ChartHub in project identity and package IDs.
- Some backend/API references still intentionally use RhythmVerse naming where tied to the remote service contract.

## License

See `LICENSE.txt`.
