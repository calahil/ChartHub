using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;

using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class MainViewModelTests
{
    [Fact]
    public void Constructor_InDesktopMode_ShowsCloneHeroAndSettingsTabs()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: false);

        Assert.True(sut.IsDesktopMode);
        Assert.False(sut.IsCompanionMode);
        Assert.True(sut.IsCloneHeroTabVisible);
        Assert.True(sut.IsDesktopEntryTabVisible);
        Assert.True(sut.IsVolumeTabVisible);
        Assert.True(sut.IsSettingsTabVisible);
        Assert.True(sut.IsDownloadTabVisible);
    }

    [Fact]
    public void Constructor_InAndroidMode_ShowsCloneHero()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        Assert.True(sut.IsCompanionMode);
        Assert.False(sut.IsDesktopMode);
        Assert.True(sut.IsCloneHeroTabVisible);
        Assert.True(sut.IsDesktopEntryTabVisible);
        Assert.True(sut.IsVolumeTabVisible);
        Assert.True(sut.IsSettingsTabVisible);
        Assert.True(sut.IsDownloadTabVisible);
    }

    [Fact]
    public void GoCloneHeroCommand_OnAndroid_SelectsCloneHeroTab()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        sut.GoCloneHeroCommand.Execute(null);

        Assert.Equal(3, sut.SelectedMainTabIndex);
    }

    [Fact]
    public void GoSettingsCommand_SelectsSettingsTab()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        sut.GoSettingsCommand.Execute(null);

        Assert.Equal(6, sut.SelectedMainTabIndex);
    }

    [Fact]
    public void GoDesktopEntryCommand_SelectsDesktopEntryTab()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        sut.GoDesktopEntryCommand.Execute(null);

        Assert.Equal(4, sut.SelectedMainTabIndex);
    }

    [Fact]
    public void GoVolumeCommand_SelectsVolumeTab()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        sut.GoVolumeCommand.Execute(null);

        Assert.Equal(5, sut.SelectedMainTabIndex);
    }

    [Fact]
    public void AndroidNavigationCommand_ClosesPaneAfterNavigation()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        sut.ShowAndroidFiltersInFlyoutCommand.Execute(null);
        sut.GoDownloadsCommand.Execute(null);

        Assert.Equal(2, sut.SelectedMainTabIndex);
        Assert.False(sut.IsAndroidNavPaneOpen);
        Assert.False(sut.IsAndroidFlyoutFiltersMode);
        Assert.True(sut.IsAndroidNavListMode);
    }

    [Fact]
    public void SharedDownloadState_UpdatesHasSharedDownloadsFlags()
    {
        var queue = new SharedDownloadQueue();
        MainViewModel sut = CreateMainViewModel(isAndroid: true, sharedDownloadQueue: queue);

        Assert.False(sut.HasSharedDownloads);
        Assert.True(sut.NoSharedDownloads);

        queue.Downloads.Add(new DownloadFile("song.zip", "/tmp", "https://example.test/song.zip", 42));

        Assert.True(sut.HasSharedDownloads);
        Assert.False(sut.NoSharedDownloads);
    }

    [Fact]
    public void GoVirtualControllerCommand_SelectsControllerTab()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        sut.GoVirtualControllerCommand.Execute(null);

        Assert.Equal(7, sut.SelectedMainTabIndex);
    }

    [Fact]
    public void GoVirtualTouchPadCommand_SelectsTouchPadTab()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        sut.GoVirtualTouchPadCommand.Execute(null);

        Assert.Equal(8, sut.SelectedMainTabIndex);
    }

    [Fact]
    public void GoVirtualKeyboardCommand_SelectsKeyboardTab()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        sut.GoVirtualKeyboardCommand.Execute(null);

        Assert.Equal(9, sut.SelectedMainTabIndex);
    }

    [Fact]
    public void ToggleInputAccordionCommand_TogglesIsInputAccordionExpanded()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        Assert.False(sut.IsInputAccordionExpanded);

        sut.ToggleInputAccordionCommand.Execute(null);
        Assert.True(sut.IsInputAccordionExpanded);

        sut.ToggleInputAccordionCommand.Execute(null);
        Assert.False(sut.IsInputAccordionExpanded);
    }

    private static MainViewModel CreateMainViewModel(bool isAndroid, SharedDownloadQueue? sharedDownloadQueue = null)
    {
        ConstructorInfo? constructor = typeof(MainViewModel).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(RhythmVerseViewModel),
                typeof(EncoreViewModel),
                typeof(SharedDownloadQueue),
                typeof(DownloadViewModel),
                typeof(CloneHeroViewModel),
                typeof(DesktopEntryViewModel),
                typeof(VolumeViewModel),
                typeof(SettingsViewModel),
                typeof(VirtualControllerViewModel),
                typeof(VirtualTouchPadViewModel),
                typeof(VirtualKeyboardViewModel),
                typeof(Action<Action>),
                typeof(bool),
                typeof(IStatusBarService),
            ],
            modifiers: null);

        Assert.NotNull(constructor);

        SharedDownloadQueue queue = sharedDownloadQueue ?? new SharedDownloadQueue();

        return (MainViewModel)constructor.Invoke([
            CreateUninitialized<RhythmVerseViewModel>(),
            CreateUninitialized<EncoreViewModel>(),
            queue,
            CreateUninitialized<DownloadViewModel>(),
            CreateUninitialized<CloneHeroViewModel>(),
            CreateUninitialized<DesktopEntryViewModel>(),
            CreateUninitialized<VolumeViewModel>(),
            CreateUninitialized<SettingsViewModel>(),
            null,
            null,
            null,
            (Action<Action>)(action => action()),
            isAndroid,
            null,
        ]);
    }

    private static T CreateUninitialized<T>() where T : class
    {
        return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }
}
