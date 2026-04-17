# Contributing

Before making any changes, read the authoritative governance documents:

- [`.governance/AGENTS.md`](https://github.com/calahilstudios/charthub/blob/main/.governance/AGENTS.md) — contribution rules and definition of done
- [`.governance/architecture.md`](https://github.com/calahilstudios/charthub/blob/main/.governance/architecture.md) — MVVM boundaries
- [`.github/copilot-instructions.md`](https://github.com/calahilstudios/charthub/blob/main/.github/copilot-instructions.md) — agent behavior rules

---

## Requirements

- .NET SDK 10.0
- For Android builds: Android SDK at `$HOME/Android/Sdk`

---

## Local Development

From the repository root:

```bash
dotnet build ChartHub/ChartHub.csproj
dotnet run --project ChartHub/ChartHub.csproj --framework net10.0
```

---

## Validation Gates

All changes must pass before merging:

```bash
# Format check
dotnet format ChartHub.sln --verify-no-changes --severity error --no-restore

# Release build (zero warnings, zero errors)
dotnet build ChartHub.sln --configuration Release --no-restore

# Test suite
dotnet test ChartHub.Tests/ChartHub.Tests.csproj --configuration Release --no-build
```

---

## Localization

All user-facing strings must use localization resources. Hardcoded user-facing literals in production code are forbidden.

**Allowed exceptions**: icon/resource URIs, telemetry keys, protocol/format constants, test-only literals.

---

## Android Builds

Build the Android target:

```bash
dotnet build ChartHub/ChartHub.csproj \
  -p:EnableAndroid=true \
  -f net10.0-android \
  -p:AndroidSdkDirectory=$HOME/Android/Sdk
```

All Android-specific code and usings must be guarded with `#if ANDROID`.

---

## Definition of Done

A change is complete when:

1. `dotnet format` passes with no changes.
2. Release build passes with zero warnings and zero errors.
3. Relevant tests are added or updated.
4. Tests pass.
5. No new warning suppressions were introduced.
6. No banned APIs are used.
7. No new hardcoded user-facing strings in production code.
8. Final summary covers: files changed, why, validation results, tests, suppressions.
