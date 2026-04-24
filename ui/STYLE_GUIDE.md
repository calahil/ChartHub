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
- `ThemeForegroundBrush`
- `ThemeForegroundLowBrush`
- `SemanticWarning`

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

## Interaction Rules

### 1) Icon-Only Actions
- Must include `ToolTip.Tip`.
- Keep hit area at least `40x40` for top-level actions.

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
4. Icon-only actions are `40x40`, `CornerRadius=10`, tooltip present.
5. SVG assets use `fill="currentColor"`.
6. No unapproved hardcoded color hex values in AXAML.
7. Secondary text and semantic states use theme tokens.
8. Android control rows align cleanly and avoid touching controls.

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
