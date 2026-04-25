# ChartHub Client UI Style Guide

## Purpose
This guide defines the visual and interaction baseline for ChartHub client views.
The baseline is derived from the current Android DownloadsView and the Android top app bar in MainView.

Goals:
- Keep new features visually consistent.
- Make Android and Desktop feel like one product.
- Prevent one-off colors, spacing, and control shapes.

## Source Of Truth
- Layout and control patterns:
	- `ChartHub/Views/DownloadView.axaml`
	- `ChartHub/Views/MainView.axaml`
- Theme tokens:
	- `ChartHub/Themes/CatppuccinMacchiato/Theme.axaml`
- SVG tint behavior:
	- `ChartHub/Utilities/ImageTools.cs`

## Core Visual Language

### 1) Spacing Scale
Use these values as the default rhythm:
- `4` micro gap (tight label/value clusters)
- `6` compact card internals
- `8` standard vertical rhythm
- `10` standard control padding/gap
- `12` page edge margin

Rule:
- Prefer only the scale above.
- If a new value is needed, document why in PR notes.

### 2) Corner Radius Scale
- `6`: compact helper buttons (example: log toggle)
- `8`: cards, grouped containers, empty states
- `10`: primary interactive controls (icon buttons, combo boxes)
- `12`: large floating/top-level desktop button only when needed

Rule:
- Do not mix multiple radii within a single control family.

### 3) Color And Theme Tokens
Always use theme resources. Do not hardcode hex colors in AXAML control properties.

Primary tokens used in DownloadsView:
- `ThemeBackgroundBrush`
- `ThemeBorderMidBrush`
- `ThemeControlLowBrush`
- `DataFieldBackgroundBrush`
- `ThemeForegroundBrush`
- `ThemeForegroundLowBrush`
- `SemanticWarning`

Data-field chip rule:
- Small data chips/badges inside list cards (counts, tags, compact metadata pills) should use `DataFieldBackgroundBrush`.
- Do not use `ThemeControlMidBrush` for these chips, because it can visually collide with hover/selected card states.

### 4) Typography
- App default font family is CaskaydiaCove Nerd Font from `AppDefaultFontFamily`.
- Content hierarchy in DownloadsView:
	- Title: default size, `SemiBold`
	- Supporting metadata/status: `FontSize=11`
	- Log lines: `FontSize=10`, `FontFamily=Monospace`

Rule:
- Use weight and tokenized foreground to create hierarchy before increasing font size.

## Icon System

### 1) SVG Requirements
All UI SVGs must use:
- `fill="currentColor"`

Why:
- `AssetPathToImageConverter` replaces `currentColor` with the active icon tint color.

### 2) Icon Tint Rule
- Default icon tint comes from converter default `#C6A0F6` (Macchiato mauve).
- Do not pass `ConverterParameter` for normal controls.
- Use `ConverterParameter` only for intentionally semantic icons and document the exception.

### 3) Icon Button Geometry
Canonical icon action button (Android toolbar pattern):
- `Width=40`, `Height=40`
- `CornerRadius=10`
- Icon size: `20 x 20`
- Tooltip required for icon-only actions

This is the style used by:
- Android hamburger button in MainView
- Android auth button in MainView
- Android install button in DownloadsView

## Component Patterns (Baseline)

### 1) Page Shell
- Root margin: `12`
- Top section spacing: `8`
- Secondary row spacing: `10`

### 1.1) Desktop Split Layout Ownership
Desktop views that use a `SplitView` must separate actions by ownership:
- Pane-owned actions stay with the pane chrome or pane rail.
- Content-owned actions stay in the content/header region and must not float over the pane.

Specific rule from MainView:
- The desktop filter toggle is pane-owned.
- The desktop auth/sign-out button is content-owned.
- Do not place the desktop auth button as a root-level floating overlay if the desktop filter pane opens near the same edge.

Alignment rule when these actions are visually adjacent:
- Use the same top offset.
- Use the same button size: `40x40`.
- Use the same icon size: `20x20`.
- Use the same corner radius: `10`.

Reason:
- This prevents action ownership from becoming visually ambiguous when the pane opens.
- It avoids the sign-out button appearing to move into or belong to the filter pane.

### 2) Filter/Control Row
- Left-aligned filter labels and controls.
- Label vertical alignment centered with control.
- ComboBox corner radius: `10`.

### 3) Content Container
- Outer list container:
	- Border thickness `1`
	- Border brush `ThemeBorderMidBrush`
	- Corner radius `8`
	- Padding `10`

### 3.1) ListBox Card Surfaces
ListBox-based card views must preserve rounded corners in every interaction state:
- default
- pointer over
- selected
- selected plus pointer over

Rules:
- The visible item surface should be a rounded card `Border` with `Classes="song-card"`.
- All state-driven background and border changes (base, hover, selected, selected+hover) must be applied via selector styles targeting `Border.song-card`, not via local property values on the Border. This ensures the selection styles can win specificity over the card surface.
- The outer ListBox shell should also be rounded:
	- `CornerRadius="8"`
	- `BorderBrush="{StaticResource ThemeBorderMidBrush}"`
	- `Padding="10"`
	- `ClipToBounds="True"`
- The inner card should use:
	- `Classes="song-card"`
	- `CornerRadius="8"`
	- `Padding="10"`
	- No local `Background` or `BorderBrush` — these come entirely from selector styles
- The ListBox chrome must not visually fight the rounded shell.

Required selector styles for every view using `rounded-selection-list`:
```xml
<Style Selector="ListBox.rounded-selection-list > ListBoxItem Border.song-card">
    <Setter Property="Background" Value="{StaticResource ThemeControlLowBrush}" />
    <Setter Property="BorderBrush" Value="{StaticResource ThemeBorderMidBrush}" />
</Style>
<Style Selector="ListBox.rounded-selection-list > ListBoxItem:pointerover Border.song-card">
    <Setter Property="Background" Value="{StaticResource ThemeControlMidBrush}" />
    <Setter Property="BorderBrush" Value="{StaticResource ThemeAccentBrush2}" />
</Style>
<Style Selector="ListBox.rounded-selection-list > ListBoxItem:selected Border.song-card">
    <Setter Property="Background" Value="{StaticResource ListBoxItemBackgroundSelected}" />
    <Setter Property="BorderBrush" Value="{StaticResource ThemeAccentBrush}" />
</Style>
<Style Selector="ListBox.rounded-selection-list > ListBoxItem:selected:pointerover Border.song-card">
    <Setter Property="Background" Value="{StaticResource ListBoxItemBackgroundSelectedPointerOver}" />
    <Setter Property="BorderBrush" Value="{StaticResource ThemeAccentBrush}" />
</Style>
```

### 3.2) ListBox Hover And Selected State Ownership
For rounded card list items, the hover and selected visuals must align to the visible card bounds.

Rules:
- Hover and selected visuals should be painted on the rounded card layer, not only on the default `ListBoxItem` chrome.
- If the platform theme still paints a larger square-looking `ListBoxItem` highlight, suppress or neutralize the container background for that list and let the card layer own the visual state.
- If the platform already matches the visible item bounds correctly, preserve that behavior.

### 3.3) ListBox Item Spacing Rule
Inter-item spacing must belong to the `ListBoxItem` container, not the inner card.

Reason:
- If the bottom gap lives on the inner card, hover or selected highlights can visually extend into that gap.
- Putting the gap on the item container keeps the highlight bounded to the actual item.

Rules:
- Inner card margin should be `0` when the item is using rounded hover and selected styling.
- Item-to-item gap should be applied at the `ListBoxItem` level.
- Standard gap between rounded list items: `6`.

### 3.4) ListBox Hover And Selection Contrast
Hover and selection must be visibly distinct from the base card state.

Recommended behavior:
- Base card: `ThemeControlLowBrush`
- Hover card: stronger contrast than base, not a near-match
- Selected card: clear selected fill plus stronger border
- Selected plus pointer over: slightly stronger than selected

Rule:
- Do not use a hover background that is visually indistinguishable from the card's normal background.

### 4) Queue Item Card
- Card style:
	- Margin bottom `6`
	- Padding `10`
	- Corner radius `8`
	- Background `ThemeControlLowBrush`
	- Border `ThemeBorderMidBrush`, thickness `1`

### 5) Status And Feedback
- Secondary text/status: `ThemeForegroundLowBrush`
- Warning text: `SemanticWarning`
- Empty states use same card shell styling as list cards.

### 5.1) Data-Field Chips Inside ListBox Cards
Small metadata chips and badges inside ListBox cards must use `DataFieldBackgroundBrush`.

Reason:
- Chips should remain visually stable while the card background changes on hover or selection.
- Reusing `ThemeControlMidBrush` for chips can make hover and selected states harder to read.

Examples:
- metadata pills
- compact counters
- compact tag badges
- button surfaces that are visually acting as data-field chips inside a card

## Interaction Rules

### 1) Icon-Only Actions
- Must include `ToolTip.Tip`.
- Keep hit area at least `40x40` for top-level actions.
- For `40x40` icon buttons, use `Padding="0"` and a `26x26` icon image.
- Keep the icon centered and use `Stretch="Uniform"` when sizing the image explicitly.

### 2) Disabled State
- Use built-in control disabled visuals from theme.
- Never fake disabled state by reducing opacity manually unless absolutely required.

### 3) Scroll Behavior
- Vertical scroll enabled for content lists.
- Horizontal scrolling disabled unless content type explicitly requires it.

## Do / Do Not

Do:
- Reuse existing resource tokens.
- Reuse canonical button and card geometry.
- Keep icon color pipeline through `currentColor`.

Do not:
- Hardcode icon fills (for example `#FFFFFF`) in SVGs.
- Hardcode one-off AXAML hex colors for standard UI.
- Mix arbitrary corner radii in the same view.
- Introduce text buttons where toolbar icon buttons are the established pattern.

## Copy-Forward Checklist For New Views
Before merging a new view or major UI update, verify:

1. Spacing uses the scale `4/6/8/10/12`.
2. Radius uses `6/8/10/12` appropriately.
3. Cards use tokenized border/background brushes.
4. Icon-only actions are `40x40`, `CornerRadius=10`, tooltip present, and use `26x26` icons with `Padding=0`.
5. SVG assets use `fill="currentColor"`.
6. No unapproved hardcoded color hex values in AXAML.
7. Secondary text and semantic states use theme tokens.
8. Android control rows align cleanly and avoid touching controls.
9. Desktop `SplitView` actions are placed in the correct ownership zone: pane actions in pane chrome, app actions in content/header chrome.
10. Rounded ListBox cards keep hover and selected visuals inside the visible item bounds.
11. Rounded ListBox inter-item spacing is applied at the `ListBoxItem` level, not the inner card.
12. ListBox card chips and badges use `DataFieldBackgroundBrush`.
13. New song source views follow the Song Source View Pattern: dual-mode layout, ViewModel contract, full style boilerplate, `song-card` class on inner Border, DataContext cast matches actual ViewModel type.

## Song Source View Pattern

This section defines the canonical structure for any view that browses a song source (RhythmVerse, Encore, or any future source added to ChartHub).

Both existing implementations — `RhythmVerseView.axaml` and `EncoreView.axaml` — are the reference implementations for this pattern.
When wiring up a new source, copy the structure from one of those files and adapt only the ViewModel type, column bindings, and source-specific columns.

---

### Architecture

A song source view has:
- A **ViewModel** that owns search state, song list, selection, and download commands.
- A **View** that presents two platform-specific layouts inside the same `UserControl`: one `IsDesktopMode` layout and one `IsCompanionMode` (Android/mobile) layout.
- Shared child controls embedded in the item template: `SongInfoControl`, `SongRatingControl`.
- A shared download queue strip above the list: `SharedDownloadCardsView`.

### ViewModel Contract

The ViewModel for any song source view must expose at minimum:

| Property | Purpose |
|---|---|
| `IsDesktopMode` | Drives desktop layout visibility |
| `IsCompanionMode` | Drives mobile layout visibility |
| `SearchText` | Two-way bound to search TextBox |
| `DataItems` | `IEnumerable` bound to ListBox `ItemsSource` |
| `SelectedSong` | Two-way bound to ListBox `SelectedItem` |
| `HasResults` | Controls results list visibility |
| `NoResults` | Controls empty state visibility |
| `IsPlaceholder` | Controls loading state visibility |
| `Downloads` | Bound to `SharedDownloadCardsView.Downloads` |
| `HasActiveDownloads` | Bound to `SharedDownloadCardsView.HasActiveDownloads` |
| `PageStrings` | Localized string container for the view |
| `RefreshCommand` | Refresh/search trigger |
| `DownloadSongCommand` | Download action, parameterized with the song item |
| `CancelDownloadCommand` | Cancel a queued download |
| `ClearDownloadCommand` | Clear a completed download |

Source-specific commands (e.g. `ViewCreatorCommand`) are optional. If the source does not support a command, replace the `Button` with a styled read-only chip (`Border.song-card` pattern for data display, `DataFieldBackgroundBrush` background).

---

### Desktop Layout Structure

```
Grid RowDefinitions="Auto,*" (IsVisible="{Binding IsDesktopMode}")
├── Grid Row="0" — Search and filter bar
│   ├── TextBox (search, CornerRadius="10", Height="40")
│   ├── [Source-specific filter controls]
│   └── Button (refresh, 40x40, CornerRadius="10")
│
└── Grid Row="1" ColumnDefinitions="*,Auto" RowDefinitions="Auto,*"
    ├── SharedDownloadCardsView (Row="0", Margin="0,0,0,10")
    └── Border Row="1" (outer list container)
            BorderThickness="1"
            BorderBrush="{StaticResource ThemeBorderMidBrush}"
            CornerRadius="8"
            ClipToBounds="True"
            Padding="10"
        └── ListBox x:Name="[Source]SongsListBox"
                Classes="rounded-selection-list"
                Background="{StaticResource ListBoxBackground}"
                BorderThickness="0"
```

**Desktop song card columns (standard 5-column layout):**

| Column | Content |
|---|---|
| 0 | Album art (`Border` + `Image`, `CornerRadius="10"`, `Background=ThemeControlMidBrush`) |
| 1 | `SongInfoControl` |
| 2 | `SongRatingControl` |
| 3 | Author / creator info (chip or button) |
| 4 | Download button + metadata chips (InLibrary badge, counts) |

Column proportions used by reference implementations: `128, 2.6*, 2.1*, 2*, 2*`

---

### Mobile (Companion) Layout Structure

```
Grid RowDefinitions="Auto,*" (IsVisible="{Binding IsCompanionMode}", Margin="10")
├── Grid Row="0" ColumnDefinitions="*,Auto" — Search bar
│   ├── TextBox (search)
│   └── Button (refresh)
│
└── Grid Row="1" — Content area
    ├── [Loading state: ProgressBar + TextBlock]
    ├── [Empty state: TextBlock]
    └── Grid (IsVisible="{Binding HasResults}") RowDefinitions="Auto,*"
        ├── SharedDownloadCardsView Row="0" (IsCompact="True", Margin="0,0,0,8")
        └── Border Row="1" (outer list container)
                CornerRadius="8"
                ClipToBounds="True"
                Padding="8"
            └── ListBox x:Name="MobileSongsListBox"
                    Classes="rounded-selection-list"
                    Background="{StaticResource ListBoxBackground}"
```

**Mobile song card rows (standard 3-row layout):**

| Row | Content |
|---|---|
| Row 0, Col 0 | Album art thumbnail (48x48) |
| Row 0, Col 1 | `SongInfoControl` |
| Row 0, Col 2 | Download button (32px wide) |
| Row 1, Col 0–2 | `SongRatingControl` (full width) |
| Row 2, Col 0–2 | InLibrary badge (visible when applicable) |

---

### Required Style Boilerplate

Every song source view `UserControl.Styles` block must include the full `rounded-selection-list` + `song-card` selector set:

```xml
<UserControl.Styles>
    <!-- ListBoxItem container reset -->
    <Style Selector="ListBox.rounded-selection-list > ListBoxItem">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Padding" Value="0" />
        <Setter Property="CornerRadius" Value="8" />
        <Setter Property="ClipToBounds" Value="True" />
    </Style>

    <!-- Song card state styles (all four states required) -->
    <Style Selector="ListBox.rounded-selection-list > ListBoxItem Border.song-card">
        <Setter Property="Background" Value="{StaticResource ThemeControlLowBrush}" />
        <Setter Property="BorderBrush" Value="{StaticResource ThemeBorderMidBrush}" />
    </Style>
    <Style Selector="ListBox.rounded-selection-list > ListBoxItem:pointerover Border.song-card">
        <Setter Property="Background" Value="{StaticResource ThemeControlMidBrush}" />
        <Setter Property="BorderBrush" Value="{StaticResource ThemeAccentBrush2}" />
    </Style>
    <Style Selector="ListBox.rounded-selection-list > ListBoxItem:selected Border.song-card">
        <Setter Property="Background" Value="{StaticResource ListBoxItemBackgroundSelected}" />
        <Setter Property="BorderBrush" Value="{StaticResource ThemeAccentBrush}" />
    </Style>
    <Style Selector="ListBox.rounded-selection-list > ListBoxItem:selected:pointerover Border.song-card">
        <Setter Property="Background" Value="{StaticResource ListBoxItemBackgroundSelectedPointerOver}" />
        <Setter Property="BorderBrush" Value="{StaticResource ThemeAccentBrush}" />
    </Style>

    <!-- Mobile: item spacing at container level (not card level) -->
    <Style Selector="ListBox#MobileSongsListBox > ListBoxItem">
        <Setter Property="Margin" Value="0,0,0,6" />
    </Style>
    <!-- Mobile: suppress container-level highlight, let song-card own the state -->
    <Style Selector="ListBox#MobileSongsListBox > ListBoxItem:pointerover">
        <Setter Property="Background" Value="Transparent" />
    </Style>
    <Style Selector="ListBox#MobileSongsListBox > ListBoxItem:selected">
        <Setter Property="Background" Value="Transparent" />
    </Style>
    <Style Selector="ListBox#MobileSongsListBox > ListBoxItem:selected:pointerover">
        <Setter Property="Background" Value="Transparent" />
    </Style>
</UserControl.Styles>
```

---

### Item Template Rules

Inner card `Border` on both desktop and mobile:
- Must carry `Classes="song-card"` — this is what the selector styles target
- `CornerRadius="8"`
- `BorderThickness="1"`
- `Padding="10"` (desktop) or `Padding="8"` (mobile)
- `Margin="0"` — spacing belongs to the `ListBoxItem` container, never the card
- No local `Background` or `BorderBrush` — state comes from the selector styles above

All metadata chips, badge pills, and data counters inside the card:
- `Background="{StaticResource DataFieldBackgroundBrush}"`
- `CornerRadius="10"`
- `Padding="8,2"`
- `BorderThickness="1"`, `BorderBrush="{StaticResource ThemeBorderMidBrush}"`

If a source-specific command does not exist on the ViewModel, show the data as a read-only chip (a styled `Border`) rather than a `Button`. Do not leave a broken command binding.

---

### DataContext Cast Rule

ViewModel type casts in item template bindings (`#ListBoxName.((viewmodels:XViewModel)DataContext).Property`) must match the actual ViewModel type for that view.

Copying a template from another source view and forgetting to update these casts will silently break `IsDesktopMode`, `IsCompanionMode`, and `PageStrings` — causing the card chip layout to never render.

Always verify: every cast in the item template refers to the correct ViewModel class.

---

### Reference Implementations

- `ChartHub/Views/RhythmVerseView.axaml` — primary reference
- `ChartHub/Views/EncoreView.axaml` — secondary reference

---

## Unification Plan For Remaining Views
Apply this order to align the rest of the app quickly:

1. Top bars and action buttons
- Normalize all icon actions to the 40x40/10-radius pattern.

2. Filter rows
- Left-align labels and controls where practical.
- Normalize ComboBox radius to `10`.

3. Card/list surfaces
- Standardize to `8` radius, `10` padding, `ThemeBorderMidBrush` border.

4. Icon assets
- Audit all SVGs for `currentColor` and remove fixed fills.

5. Status text
- Replace ad hoc colors with semantic tokens.

## Notes For Future Tweaks
When design direction changes, update this file first, then implement view updates.
Treat this document as the contract for visual consistency.
