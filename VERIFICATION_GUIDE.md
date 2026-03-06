# .NET 10 Linux Alignment - Verification & Next Steps

## ✅ Changes Completed

All critical and high-priority issues have been addressed:

1. ✅ **SettingsManager.csproj** - Removed MAUI dependencies, cleaned platform settings
2. ✅ **Services/Nautilus.cs** - Added platform detection, wrapped Windows P/Invoke calls
3. ✅ **Utilities/Initializer.cs** - Platform-aware executable names and file paths
4. ✅ **App.axaml.cs** - Refactored to use proper Avalonia pattern

---

## 📋 Verification Steps to Run

### Step 1: Build Verification
Run the build to ensure all changes compile correctly:

```bash
cd /srv/games/source/rhythmverse-client/rhythmverseclient
dotnet build RhythmVerseClient/RhythmVerseClient.csproj
dotnet build SettingsManager/SettingsManager.csproj
```

**Expected Result:** ✅ Build succeeds with no errors or warnings

---

### Step 2: Runtime Testing (Linux)

#### Option A: Run directly
```bash
dotnet run --project RhythmVerseClient/RhythmVerseClient.csproj
```

#### Option B: Use included task
Press `Ctrl+Shift+B` in VS Code to run the build task, then use the run task for debugging.

**Expected Behavior:**
- Application window opens with Avalonia UI
- No crashes from Windows API calls
- Proper window rendering on X11/Wayland

---

### Step 3: Functional Testing

Test these specific areas:

**1. Application Startup**
- [ ] App launches without errors
- [ ] Main window displays correctly
- [ ] UI renders properly with Fluent theme

**2. File System Operations**
- [ ] Verify correct paths created:
  - Linux: `~/.local/share/Clone Hero/Songs`
  - Check actual location with: `ls -la ~/.local/share/`
- [ ] AppData directories created correctly

**3. Nautilus Integration (if applicable)**
- [ ] Check if Nautilus executable detection works
- [ ] Verify it looks for `Nautilus` (no .exe) on Linux
- [ ] Test launching if Nautilus binary is available

**4. Services Initialization**
- [ ] ResourceWatcher initializes without errors
- [ ] RhythmVerseApiClient connects properly
- [ ] Google Drive integration works (if configured)

---

## 🔍 Code Review Checklist

Verify the changes made:

### Nautilus.cs
```csharp
// ✓ Should have platform detection
string executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
    ? "Nautilus.exe" 
    : "Nautilus";

// ✓ Should have Windows conditional compilation
#if WINDOWS
User32.ShowWindow(Hwnd, 2);
#endif

// ✓ User32 class should be wrapped
#if WINDOWS
public static partial class User32 { ... }
#endif
```

### Initializer.cs
```csharp
// ✓ Should detect platform for Clone Hero path
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    CloneHeroDataDir = Path.Combine(..., "Clone Hero");
}
else
{
    // Linux path using XDG conventions
    CloneHeroDataDir = Path.Combine(homeDir, ".local", "share", "Clone Hero");
}
```

### App.axaml.cs
```csharp
// ✓ Should use proper Avalonia pattern
if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
{
    desktopLifetime.MainWindow = new MainView();
}
```

### SettingsManager.csproj
```xml
<!-- ✓ Should have single net10.0 target -->
<TargetFramework>net10.0</TargetFramework>

<!-- ✓ Should NOT have these -->
<!-- REMOVED: Microsoft.Maui.Controls -->
<!-- REMOVED: Cross-platform OS version settings -->
```

---

## 📊 Current Status

| Component | Status | Evidence |
|-----------|--------|----------|
| .NET 10 Target | ✅ OK | `<TargetFramework>net10.0</TargetFramework>` |
| Avalonia Setup | ✅ OK | v11.1.0, X11 support included |
| Windows Code | ✅ FIXED | All wrapped in `#if WINDOWS` |
| Linux Paths | ✅ FIXED | XDG convention support |
| Dependencies | ✅ OK | MAUI removed, .NET 10 compatible |
| XAML Setup | ✅ OK | Proper Avalonia pattern in App.cs |

---

## ⚠️ Known Limitations & Considerations

### Windows-Only Features
The following are now properly guarded for Windows-only:
- `User32` P/Invoke methods (window management)
- Nautilus window control operations
- Will compile/run on Linux but those features won't be available

### Linux-Specific Notes
1. **Nautilus Binary:** The application looks for a Nautilus executable on Linux
   - Verify the Nautilus build includes Linux binaries
   - Or make Nautilus optional/conditional

2. **Clone Hero Data:** 
   - Windows uses: `Documents/Clone Hero/`
   - Linux uses: `~/.local/share/Clone Hero/` (XDG compliant)
   - Users need to be aware of this difference

3. **Display Server:**
   - X11 is configured (primary)
   - Wayland support may work via Avalonia but not explicitly tested
   - Can be adjusted in Program.cs if needed

---

## 🚀 Deployment Checklist

### Before Final Release

- [ ] Build successfully on Linux
- [ ] Run all unit tests (if any exist)
- [ ] Manual testing on Ubuntu/Fedora/Debian
- [ ] Document Linux-specific setup (e.g., Nautilus binary requirements)
- [ ] Test with different desktop environments (GNOME, KDE, etc.)
- [ ] Package as .AppImage or deb/rpm if desired
- [ ] Update README with Linux installation instructions

### Optional Enhancements

- [ ] Create platform abstraction layer for common OS operations
- [ ] Add automated CI/CD for Linux builds
- [ ] Create comprehensive platform-specific test suite
- [ ] Document XDG Base Directory compliance
- [ ] Consider publishing snap package for wider Linux compatibility

---

## 📚 Useful Resources

### .NET on Linux
- [.NET on Linux](https://docs.microsoft.com/en-us/dotnet/core/install/linux)
- [.NET 10 Release Notes](https://github.com/dotnet/core/releases)

### Avalonia on Linux
- [Avalonia Linux Support](https://docs.avaloniaui.net/guides/platforms/linux)
- [Avalonia X11 Platform](https://docs.avaloniaui.net/guides/platforms/linux/x11-backend)

### Linux File System Standards
- [XDG Base Directory Specification](https://specifications.freedesktop.org/basedir-spec/basedir-spec-latest.html)
- [Linux File System Hierarchy](https://www.tldp.org/LDP/Linux-Filesystem-Hierarchy/html/)

---

## 🎯 Success Criteria

Your codebase will be fully aligned with .NET 10 & Avalonia UI for Linux when:

1. ✅ Code compiles without errors on Linux
2. ✅ Application runs and displays window on Linux X11
3. ✅ File paths are created in correct locations
4. ✅ No Windows-specific code runs on Linux
5. ✅ All tests pass (if applicable)
6. ✅ No runtime exceptions on platform-specific features

---

## 📝 Questions or Issues?

If you encounter any issues:

1. Check build output for specific errors
2. Review the analysis and changes documents
3. Verify all files were modified correctly
4. Check that RuntimeInformation is properly imported
5. Ensure preprocessor directives are balanced

---

**Last Updated:** 2026-03-05  
**Changes Made:** 4 files modified  
**Status:** Ready for testing and verification  

