# Portable Visual Infrastructure & Design System

**Source project:** SelfishNet v2 — WPF shell (`src/SelfishNetV2.App`)
**Extraction date:** 2026-10-19
**Document version:** 1.0.0
**Status:** Standalone reference. This file is additive only; it does not modify the source project.

> **What this is:** a complete, self-contained description of the app's visual layer — the
> technology, architecture, design tokens, full theme dictionaries, component specs, the
> custom chart control, and a step-by-step porting guide.
>
> **What it is not:** network/ARP/engine logic, localization content, installer/publish
> details, or a NuGet package. The XAML and C# blocks below are directly copy-pasteable
> into another WPF project after namespace adjustments.

---

## Contents

1. [At a glance](#1-at-a-glance)
2. [Technology inventory](#2-technology-inventory)
3. [Visual architecture](#3-visual-architecture)
4. [Design tokens](#4-design-tokens)
5. [Complete theme dictionaries (copy-paste kit)](#5-complete-theme-dictionaries-copy-paste-kit)
6. [Component catalogue](#6-component-catalogue)
7. [Custom chart control](#7-custom-chart-control)
8. [Theme engine source](#8-theme-engine-source)
9. [App shell blueprint](#9-app-shell-blueprint)
10. [Porting to another project](#10-porting-to-another-project)
11. [Adapting outside this field](#11-adapting-outside-this-field)
12. [Extension recipes](#12-extension-recipes)
13. [Known limitations and gotchas](#13-known-limitations-and-gotchas)
14. [Source map and maintenance](#14-source-map-and-maintenance)
15. [Appendix A — resource key index](#appendix-a--resource-key-index)
16. [Appendix B — historical WinForms treatment](#appendix-b--historical-winforms-treatment)

---

## 1. At a glance

| Question | Answer |
|---|---|
| UI framework | **WPF** on **.NET 8** (`net8.0-windows`, `UseWPF=true`) |
| Markup | XAML with implicit styles and `ControlTemplate`s |
| UI pattern | MVVM (hand-rolled `ObservableObject` + `RelayCommand`) |
| Theme model | Two complete palette dictionaries (**Dark**, **Light**) swapped at runtime |
| Theme propagation | `DynamicResource` on every themed value |
| Third-party UI libraries | **None** |
| Third-party chart libraries | **None** — custom `FrameworkElement` + `DrawingContext` |
| Icons | **None** — text labels plus two Unicode arrows in the chart legend |
| Window chrome | Standard OS title bar; dark title bar via DWM P/Invoke (best effort) |
| Typography | Segoe UI Variable Text → Segoe UI fallback; Consolas for identifiers |
| DPI | PerMonitorV2 (`app.manifest`) |
| Animation | Only two: indeterminate progress bar and scrolling chart samples |
| Visual style | Flat, dense, dark-first, GitHub-like; 1 px borders, no shadows |
| Persistence | Theme name as a string in `%AppData%\...\settings.json` |
| Portability | High for any Windows desktop app; not reusable for web/mobile |

**Minimum reusable core (5 files):**

```
Themes/Dark.xaml          palette
Themes/Light.xaml         palette
Themes/Controls.xaml      control styles
Theming/ThemeManager.cs   runtime theme swap + title bar
Controls/BandwidthChartControl.cs   optional custom chart
```

Everything else in this document is convention, layout guidance, and adaptation notes.

---

## 2. Technology inventory

| Layer | Choice in this project | Notes for reuse |
|---|---|---|
| Runtime | .NET 8 (`net8.0-windows`) | Windows-only; WPF is not cross-platform |
| UI framework | WPF (`UseWPF=true`) | Hardware-accelerated vector rendering |
| Language | C# with nullable + implicit usings | No external MVVM framework |
| Markup | XAML resource dictionaries | Merged into `Application.Resources` |
| Styling | Implicit styles + keyed styles + `ControlTemplate` | Full control over every visual state |
| Theming | Palette dictionary swap + `DynamicResource` | Instant recolour, no restart |
| Typography | `Segoe UI Variable Text, Segoe UI` | Graceful fallback on older Windows |
| Monospace | `Consolas` | IP/MAC/technical identifiers only |
| Charts | Custom `FrameworkElement.OnRender` | Zero charting dependencies |
| Animation | WPF `Storyboard` + `CompositionTarget.Rendering` | No animation library |
| Window chrome | DWM `DwmSetWindowAttribute` | Immersive dark title bar (Win10 1809+) |
| Persistence | `System.Text.Json` | Theme string only; no UI framework coupling |
| DPI | `PerMonitorV2` manifest | Required for crisp rendering on mixed-DPI setups |
| Packaging | Self-contained single-file | WPF trimming is unsupported; do not enable `PublishTrimmed` |

**Deliberate non-choices**

- No Material Design, FluentUI, MahApps, HandyControl, Avalonia, or WinUI.
- No icon font or SVG icon pack.
- No chart package (LiveCharts, OxyPlot, ScottPlot).
- No `VisualStateManager`; triggers inside `ControlTemplate` are enough for this scope.
- No custom window frame or custom caption buttons.

---

## 3. Visual architecture

### 3.1 File map

```
src/SelfishNetV2.App/
├─ App.xaml                     merges Dark + Controls; app-level converter
├─ App.xaml.cs                  applies persisted theme before showing windows
├─ MainWindow.xaml              shell layout + local Card style + DataGrid columns
├─ MainWindow.xaml.cs           localization, header construction, chart wiring
├─ Views/
│  └─ AdapterWindow.xaml        modal dialog using the same tokens/styles
├─ Themes/
│  ├─ Dark.xaml                 dark palette + semantic brushes
│  ├─ Light.xaml                light palette + semantic brushes
│  └─ Controls.xaml             all control styles/templates
├─ Theming/
│  └─ ThemeManager.cs           dictionary swap + DWM title bar helper
├─ Controls/
│  └─ BandwidthChartControl.cs  custom-drawn real-time chart
└─ app.manifest                 PerMonitorV2 DPI awareness
```

### 3.2 Resource flow

```
App.xaml
  └─ MergedDictionaries
       ├─ Themes/Dark.xaml      (default palette, replaced at startup)
       └─ Themes/Controls.xaml  (styles; always present)

ThemeManager.Apply(Dark|Light)
  └─ finds the merged dictionary whose Source contains Themes/Dark.xaml or Themes/Light.xaml
       ├─ replaces it with the requested palette
       ├─ updates AppTheme.Current
       └─ applies DWM title-bar mode to every open Window

Windows / controls
  └─ reference palette keys with {DynamicResource XxxBrush}
       └─ value updates immediately when the dictionary is replaced

BandwidthChartControl
  └─ reads brushes with TryFindResource at paint time
       └─ MainWindow calls InvalidateVisual() after a theme change
```

### 3.3 Theme lifecycle

1. `App.xaml` statically merges `Dark.xaml` + `Controls.xaml`, so the designer and first frame
   always have a usable palette.
2. `App.OnStartup` loads persisted settings and calls
   `ThemeManager.Apply(settings.Theme == "Light" ? AppTheme.Light : AppTheme.Dark)`.
3. `ThemeManager` replaces the existing palette dictionary (or inserts one on first use).
   Because styles use `DynamicResource`, every bound brush updates instantly.
4. Each `Window` calls `TitleBarHelper.Apply(this, ThemeManager.Current)` from
   `SourceInitialized` so the OS title bar matches.
5. When the user toggles the theme, the view model persists the new value, calls
   `ThemeManager.Apply`, raises `ThemeChanged`, and the shell invalidates custom-drawn controls.

### 3.4 Rules that keep the system maintainable

1. **Palette dictionaries contain colours and brushes only.** No control styles, no layout.
2. **`Controls.xaml` contains styles only.** It never hard-codes a colour; it always points at a
   palette key.
3. **Use `DynamicResource` for anything that changes with the theme.**
   Use `StaticResource` only for stable keys such as `BaseButton`, `PrimaryButton`,
   `SegmentedToggle`, and the local `Card` style.
4. **One key name, two values.** Every key in `Dark.xaml` must exist in `Light.xaml`, with the same
   type. A missing key becomes an invisible/black element at runtime.
5. **Semantic names, not literal names.** `TextDimBrush`, `SurfaceAltBrush`, `DangerBrush` — not
   `GrayBrush` or `RedBrush`.
6. **Flat depth.** Separation comes from a 1 px border and a surface tint, never from shadows.
7. **Colour is never the only signal.** Status uses a dot **and** text; chart series use
   down/up arrow glyphs in the legend.
8. **No per-window palettes.** A window may define layout-only styles (e.g. `Card`) but must not
   define colours of its own.

---

## 4. Design tokens

### 4.1 Colour palettes

| Token | Dark | Light | Role |
|---|---|---|---|
| `BackgroundColor` | `#14171B` | `#F3F5F7` | Window background |
| `SurfaceColor` | `#1C2128` | `#FFFFFF` | Cards, toolbar, status bar |
| `SurfaceAltColor` | `#262C35` | `#ECEFF3` | Hover, column headers, tooltips |
| `BorderColor` | `#333B47` | `#D2D9E0` | 1 px separators and outlines |
| `TextColor` | `#E6EDF3` | `#1F2328` | Primary text |
| `TextDimColor` | `#9AA7B4` | `#5A6572` | Secondary text, labels, axes |
| `AccentColor` | `#4C8DFF` | `#0969DA` | Selection, primary action, emphasis |
| `AccentHoverColor` | `#6FA3FF` | `#2F7FE0` | Reserved for hover accents |
| `SuccessColor` | `#3FB950` | `#1A7F37` | Running/healthy state |
| `DangerColor` | `#F85149` | `#CF222E` | Stop, destructive action, errors |
| `ChartDownColor` | `#58A6FF` | `#0969DA` | Download / series A |
| `ChartUpColor` | `#3FB950` | `#1A7F37` | Upload / series B |

**Derived brushes**

| Token | Dark | Light | Definition |
|---|---|---|---|
| `SelectionBrush` | accent @ 28% | accent @ 18% | `AccentColor` with `Opacity` |
| `OverlayBrush` | `#0B0D10` @ 72% | `#FFFFFF` @ 75% | Modal busy overlay |
| `WarningBannerBrush` | `#3D2E12` | `#FFF3CD` | Warning banner background |
| `WarningTextBrush` | `#F0B429` | `#8A5A00` | Warning banner text |

**Semantic renaming for a neutral app**

If this system is reused outside network monitoring, these aliases make the intent portable:

| Original | Neutral alias |
|---|---|
| `ChartDownBrush` | `SeriesPrimaryBrush` |
| `ChartUpBrush` | `SeriesSecondaryBrush` |
| `SuccessBrush` | `PositiveBrush` |
| `DangerBrush` | `DestructiveBrush` |

### 4.2 Typography

| Role | Font | Size | Weight |
|---|---|---|---|
| Window / application default | Segoe UI Variable Text → Segoe UI | 13 | Medium |
| Body / table text | inherited | 13 | Medium |
| Secondary text | inherited | 11 | Medium |
| Section / dialog prompt | inherited | 13 | Medium |
| Column header | inherited | 13 | SemiBold |
| Card / chart title | inherited | 12 | SemiBold |
| Chart axis labels | inherited | 9 | Normal |
| Chart legend | inherited | 11 | SemiBold |
| Technical identifiers | Consolas | 13 | Medium |

Rules:

- Always declare the fallback stack: `FontFamily="Segoe UI Variable Text, Segoe UI"`.
- Keep the base size at **13 px**; the UI is intentionally dense.
- Use `SemiBold`, not `Bold`, for emphasis.
- Use monospace only for machine-generated identifiers (IP, MAC, IDs), never for prose.
- Chart text uses `FormattedText` with `VisualTreeHelper.GetDpi(this).PixelsPerDip` so it stays
  crisp under PerMonitorV2.

### 4.3 Shape and elevation

| Element | Radius |
|---|---|
| Card, dialog surface, content container | **10** |
| Button | **6** |
| Segmented toggle, TextBox, ToolTip, scrollbar thumb | **5** |
| Checkbox | **4** |
| Progress bar | **3** |

- Border thickness is always **1 px** (or a single edge for toolbar/status separators).
- No drop shadows, glows, or elevation levels.
- `UseLayoutRounding="True"` and `SnapsToDevicePixels="True"` on windows.

### 4.4 Spacing scale

The app uses a small, consistent set of values:

`2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 24`

Common compositions:

| Context | Value |
|---|---|
| Outer window/content margin | `12` |
| Dialog outer margin | `18` |
| Toolbar padding | `12,9` |
| Status bar padding | `14,8` |
| Banner padding | `14,8` |
| Card padding (charts) | `10` |
| Busy card padding | `24` |
| Gap between toolbar groups | `8`–`20` |
| Gap between chart cards | `12` |
| Button padding | `14,7` (primary CTA `16,7`) |
| Toggle padding | `12,6` |
| TextBox padding | `6,3` |
| Grid header padding | `10,8` |
| Grid cell padding | `10,4` |

### 4.5 Component metrics

| Metric | Value |
|---|---|
| Main window design size | `1280 × 780`, maximized |
| Main window minimum | `880 × 470` |
| Dialog design size | `780 × 460`, non-resizable, centred on owner |
| DataGrid row height | `34` (dialog: `38`) |
| Status dot | `9 × 9` |
| Checkbox box | `18 × 18` |
| Scrollbar width | `10` |
| Progress bar height | `6` |
| Chart strip height | `188` |
| Standard button min width | `90` |
| Primary action min width | `112` |
| Busy card width | `340` |
| Chart sample capacity | `120` samples |
| Chart grid divisions | `4` horizontal bands |
| Chart line thickness | `1.7` |
| Chart fill gradient | accent alpha `0x3C` → `0x02`, top to bottom |

### 4.6 Motion

| Motion | Specification |
|---|---|
| Indeterminate progress | 96 px indicator, `X: -96 → 340`, `1.3 s`, forever |
| Chart sample scroll | ~30 steps of `1/30` per render frame (~0.5 s perceived) |
| Theme switch | Instant; no cross-fade |
| Buttons / toggles | Instant state change; no transition |
| Busy overlay | Instant show/hide via `BooleanToVisibilityConverter` |

There is no easing library and no implicit animation. Motion exists only to communicate
"work in progress" and "new data arriving".

### 4.7 Iconography

There is no icon set. The visual language is text-first:

- Buttons are labelled with words.
- Navigation/state uses words plus a small coloured dot.
- The chart legend uses `↓` and `↑` (Unicode arrows) to pair direction with colour.

If icons are added later, use `Path` geometry with `Stroke="{DynamicResource TextBrush}"` or
`Fill`, keep a 16 px nominal box, and preserve the 1.75–2 px stroke weight used by the checkbox
check mark.

### 4.8 Visual language summary

1. Dark-first, GitHub-inspired palette with a single blue accent.
2. Flat surfaces and 1 px borders; no shadows.
3. Dense layout: 13 px base text, 34 px rows, compact padding.
4. Cards group primary content; the window itself is a five-row shell.
5. Primary action is accent-filled; destructive action is danger-filled; everything else is
   surface-coloured with a border.
6. Secondary information is dimmed rather than shrunk.
7. Technical values are monospaced and colour-coded by direction.
8. Instant theme switch is a first-class feature, not an afterthought.

---

## 5. Complete theme dictionaries (copy-paste kit)

> These three blocks are the actual dictionaries used by the app. Copy them into a
> `Themes/` folder in the target project. If you rename the files, update the two
> `Source` strings in `App.xaml` **and** the `Contains("Themes/Dark.xaml")` /
> `Contains("Themes/Light.xaml")` checks in `ThemeManager`.

### 5.1 `Themes/Dark.xaml`

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Color x:Key="BackgroundColor">#FF14171B</Color>
    <Color x:Key="SurfaceColor">#FF1C2128</Color>
    <Color x:Key="SurfaceAltColor">#FF262C35</Color>
    <Color x:Key="BorderColor">#FF333B47</Color>
    <Color x:Key="TextColor">#FFE6EDF3</Color>
    <Color x:Key="TextDimColor">#FF9AA7B4</Color>
    <Color x:Key="AccentColor">#FF4C8DFF</Color>
    <Color x:Key="AccentHoverColor">#FF6FA3FF</Color>
    <Color x:Key="SuccessColor">#FF3FB950</Color>
    <Color x:Key="DangerColor">#FFF85149</Color>
    <Color x:Key="ChartDownColor">#FF58A6FF</Color>
    <Color x:Key="ChartUpColor">#FF3FB950</Color>

    <SolidColorBrush x:Key="BackgroundBrush" Color="{StaticResource BackgroundColor}"/>
    <SolidColorBrush x:Key="SurfaceBrush" Color="{StaticResource SurfaceColor}"/>
    <SolidColorBrush x:Key="SurfaceAltBrush" Color="{StaticResource SurfaceAltColor}"/>
    <SolidColorBrush x:Key="BorderBrush" Color="{StaticResource BorderColor}"/>
    <SolidColorBrush x:Key="TextBrush" Color="{StaticResource TextColor}"/>
    <SolidColorBrush x:Key="TextDimBrush" Color="{StaticResource TextDimColor}"/>
    <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource AccentColor}"/>
    <SolidColorBrush x:Key="AccentHoverBrush" Color="{StaticResource AccentHoverColor}"/>
    <SolidColorBrush x:Key="SuccessBrush" Color="{StaticResource SuccessColor}"/>
    <SolidColorBrush x:Key="DangerBrush" Color="{StaticResource DangerColor}"/>
    <SolidColorBrush x:Key="ChartDownBrush" Color="{StaticResource ChartDownColor}"/>
    <SolidColorBrush x:Key="ChartUpBrush" Color="{StaticResource ChartUpColor}"/>
    <SolidColorBrush x:Key="SelectionBrush" Color="{StaticResource AccentColor}" Opacity="0.28"/>
    <SolidColorBrush x:Key="OverlayBrush" Color="#FF0B0D10" Opacity="0.72"/>
    <SolidColorBrush x:Key="WarningBannerBrush" Color="#FF3D2E12"/>
    <SolidColorBrush x:Key="WarningTextBrush" Color="#FFF0B429"/>
</ResourceDictionary>
```

### 5.2 `Themes/Light.xaml`

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Color x:Key="BackgroundColor">#FFF3F5F7</Color>
    <Color x:Key="SurfaceColor">#FFFFFFFF</Color>
    <Color x:Key="SurfaceAltColor">#FFECEFF3</Color>
    <Color x:Key="BorderColor">#FFD2D9E0</Color>
    <Color x:Key="TextColor">#FF1F2328</Color>
    <Color x:Key="TextDimColor">#FF5A6572</Color>
    <Color x:Key="AccentColor">#FF0969DA</Color>
    <Color x:Key="AccentHoverColor">#FF2F7FE0</Color>
    <Color x:Key="SuccessColor">#FF1A7F37</Color>
    <Color x:Key="DangerColor">#FFCF222E</Color>
    <Color x:Key="ChartDownColor">#FF0969DA</Color>
    <Color x:Key="ChartUpColor">#FF1A7F37</Color>

    <SolidColorBrush x:Key="BackgroundBrush" Color="{StaticResource BackgroundColor}"/>
    <SolidColorBrush x:Key="SurfaceBrush" Color="{StaticResource SurfaceColor}"/>
    <SolidColorBrush x:Key="SurfaceAltBrush" Color="{StaticResource SurfaceAltColor}"/>
    <SolidColorBrush x:Key="BorderBrush" Color="{StaticResource BorderColor}"/>
    <SolidColorBrush x:Key="TextBrush" Color="{StaticResource TextColor}"/>
    <SolidColorBrush x:Key="TextDimBrush" Color="{StaticResource TextDimColor}"/>
    <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource AccentColor}"/>
    <SolidColorBrush x:Key="AccentHoverBrush" Color="{StaticResource AccentHoverColor}"/>
    <SolidColorBrush x:Key="SuccessBrush" Color="{StaticResource SuccessColor}"/>
    <SolidColorBrush x:Key="DangerBrush" Color="{StaticResource DangerColor}"/>
    <SolidColorBrush x:Key="ChartDownBrush" Color="{StaticResource ChartDownColor}"/>
    <SolidColorBrush x:Key="ChartUpBrush" Color="{StaticResource ChartUpColor}"/>
    <SolidColorBrush x:Key="SelectionBrush" Color="{StaticResource AccentColor}" Opacity="0.18"/>
    <SolidColorBrush x:Key="OverlayBrush" Color="#FFFFFFFF" Opacity="0.75"/>
    <SolidColorBrush x:Key="WarningBannerBrush" Color="#FFFFF3CD"/>
    <SolidColorBrush x:Key="WarningTextBrush" Color="#FF8A5A00"/>
</ResourceDictionary>
```

### 5.3 `Themes/Controls.xaml`

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- ============ Buttons ============ -->
    <Style x:Key="BaseButton" TargetType="Button">
        <Setter Property="Background" Value="{DynamicResource SurfaceBrush}"/>
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="14,7"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="SnapsToDevicePixels" Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="bd" Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="6" Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Background" Value="{DynamicResource SurfaceAltBrush}"/>
                            <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter Property="Background" Value="{DynamicResource SelectionBrush}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType="Button" BasedOn="{StaticResource BaseButton}"/>

    <Style x:Key="PrimaryButton" TargetType="Button" BasedOn="{StaticResource BaseButton}">
        <Setter Property="Background" Value="{DynamicResource AccentBrush}"/>
        <Setter Property="Foreground" Value="White"/>
        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
    </Style>
    <Style x:Key="DangerButton" TargetType="Button" BasedOn="{StaticResource BaseButton}">
        <Setter Property="Background" Value="{DynamicResource DangerBrush}"/>
        <Setter Property="Foreground" Value="White"/>
        <Setter Property="BorderBrush" Value="{DynamicResource DangerBrush}"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
    </Style>

    <!-- ============ Toggle (engine / language pickers) ============ -->
    <Style x:Key="SegmentedToggle" TargetType="ToggleButton">
        <Setter Property="Background" Value="{DynamicResource SurfaceBrush}"/>
        <Setter Property="Foreground" Value="{DynamicResource TextDimBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="12,6"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToggleButton">
                    <Border x:Name="bd" Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="5" Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter Property="Background" Value="{DynamicResource AccentBrush}"/>
                            <Setter Property="Foreground" Value="White"/>
                            <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}"/>
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ============ TextBox ============ -->
    <Style TargetType="TextBox">
        <Setter Property="Background" Value="{DynamicResource SurfaceBrush}"/>
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="6,3"/>
        <Setter Property="SelectionBrush" Value="{DynamicResource AccentBrush}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                    <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="5" Padding="{TemplateBinding Padding}">
                        <ScrollViewer x:Name="PART_ContentHost"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ============ CheckBox ============ -->
    <Style TargetType="CheckBox">
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
        <Setter Property="VerticalContentAlignment" Value="Center"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="CheckBox">
                    <StackPanel Orientation="Horizontal">
                        <Border x:Name="box" Width="18" Height="18" CornerRadius="4"
                                Background="{DynamicResource SurfaceBrush}"
                                BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1">
                            <Path x:Name="check" Data="M 3.5,9.5 L 7.5,13 L 14.5,5" Stroke="White"
                                  StrokeThickness="2" StrokeStartLineCap="Round" StrokeEndLineCap="Round"
                                  Visibility="Collapsed" Stretch="None"/>
                        </Border>
                        <ContentPresenter Margin="7,0,0,0" VerticalAlignment="Center"/>
                    </StackPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="box" Property="Background" Value="{DynamicResource AccentBrush}"/>
                            <Setter TargetName="box" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/>
                            <Setter TargetName="check" Property="Visibility" Value="Visible"/>
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="box" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ============ DataGrid ============ -->
    <Style TargetType="DataGridColumnHeader">
        <Setter Property="Background" Value="{DynamicResource SurfaceAltBrush}"/>
        <Setter Property="Foreground" Value="{DynamicResource TextDimBrush}"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
        <Setter Property="Padding" Value="10,8"/>
        <Setter Property="BorderThickness" Value="0,0,0,1"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="DataGridColumnHeader">
                    <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Left" VerticalAlignment="Center"/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="DataGridCell">
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Padding" Value="10,4"/>
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="DataGridCell">
                    <Border Padding="{TemplateBinding Padding}" Background="Transparent">
                        <ContentPresenter VerticalAlignment="Center"/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="IsSelected" Value="True">
                <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
            </Trigger>
        </Style.Triggers>
    </Style>

    <Style TargetType="DataGridRow">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="{DynamicResource SurfaceAltBrush}"/>
            </Trigger>
            <Trigger Property="IsSelected" Value="True">
                <Setter Property="Background" Value="{DynamicResource SelectionBrush}"/>
            </Trigger>
        </Style.Triggers>
    </Style>

    <Style TargetType="DataGrid">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="GridLinesVisibility" Value="Horizontal"/>
        <Setter Property="HorizontalGridLinesBrush" Value="{DynamicResource BorderBrush}"/>
        <Setter Property="VerticalGridLinesBrush" Value="{DynamicResource BorderBrush}"/>
        <Setter Property="RowBackground" Value="Transparent"/>
        <Setter Property="AlternatingRowBackground" Value="Transparent"/>
        <Setter Property="RowHeight" Value="34"/>
        <Setter Property="CanUserAddRows" Value="False"/>
        <Setter Property="CanUserDeleteRows" Value="False"/>
        <Setter Property="CanUserResizeRows" Value="False"/>
        <Setter Property="AutoGenerateColumns" Value="False"/>
        <Setter Property="SelectionMode" Value="Single"/>
        <Setter Property="SelectionUnit" Value="FullRow"/>
        <Setter Property="ScrollViewer.CanContentScroll" Value="True"/>
    </Style>

    <!-- ============ Slim ScrollBar ============ -->
    <Style TargetType="ScrollBar">
        <Setter Property="Width" Value="10"/>
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ScrollBar">
                    <Grid x:Name="Root">
                        <Track x:Name="PART_Track" IsDirectionReversed="True">
                            <Track.DecreaseRepeatButton>
                                <RepeatButton Command="ScrollBar.PageUpCommand" Focusable="False">
                                    <RepeatButton.Template>
                                        <ControlTemplate><Border Background="Transparent"/></ControlTemplate>
                                    </RepeatButton.Template>
                                </RepeatButton>
                            </Track.DecreaseRepeatButton>
                            <Track.Thumb>
                                <Thumb>
                                    <Thumb.Template>
                                        <ControlTemplate>
                                            <Border x:Name="thumb" Background="{DynamicResource BorderBrush}" CornerRadius="5" Margin="1"/>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                            <Track.IncreaseRepeatButton>
                                <RepeatButton Command="ScrollBar.PageDownCommand" Focusable="False">
                                    <RepeatButton.Template>
                                        <ControlTemplate><Border Background="Transparent"/></ControlTemplate>
                                    </RepeatButton.Template>
                                </RepeatButton>
                            </Track.IncreaseRepeatButton>
                        </Track>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ============ ProgressBar (indeterminate, for the busy overlay) ============ -->
    <Style TargetType="ProgressBar">
        <Setter Property="Height" Value="6"/>
        <Setter Property="Background" Value="{DynamicResource SurfaceAltBrush}"/>
        <Setter Property="Foreground" Value="{DynamicResource AccentBrush}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ProgressBar">
                    <Border Background="{TemplateBinding Background}" CornerRadius="3" ClipToBounds="True">
                        <Grid>
                            <Rectangle x:Name="PART_Track"/>
                            <Rectangle x:Name="PART_Indicator" Width="96" HorizontalAlignment="Left" RadiusX="3" RadiusY="3"
                                       Fill="{TemplateBinding Foreground}">
                                <Rectangle.RenderTransform>
                                    <TranslateTransform x:Name="Slide" X="-96"/>
                                </Rectangle.RenderTransform>
                            </Rectangle>
                        </Grid>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsIndeterminate" Value="True">
                            <Trigger.EnterActions>
                                <BeginStoryboard>
                                    <Storyboard RepeatBehavior="Forever">
                                        <DoubleAnimation Storyboard.TargetName="Slide" Storyboard.TargetProperty="X"
                                                         From="-96" To="340" Duration="0:0:1.3"/>
                                    </Storyboard>
                                </BeginStoryboard>
                            </Trigger.EnterActions>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ============ ToolTip ============ -->
    <Style TargetType="ToolTip">
        <Setter Property="Background" Value="{DynamicResource SurfaceAltBrush}"/>
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="10,6"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToolTip">
                    <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="1" CornerRadius="5" Padding="{TemplateBinding Padding}">
                        <ContentPresenter/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>
```

### 5.4 `App.xaml` merge

```xml
<Application x:Class="YourApp.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/Dark.xaml"/>
                <ResourceDictionary Source="Themes/Controls.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            <BooleanToVisibilityConverter x:Key="BoolToVis"/>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

> `Controls.xaml` must be merged **after** the palette so `StaticResource` lookups inside styles
> can resolve. All actual colour references inside templates are `DynamicResource`, so the
> runtime swap still works regardless of merge order.

### 5.5 Recommended addition for a new project

Move the `Card` style out of `MainWindow.xaml` and into a shared dictionary
(`Themes/Layout.xaml`) so every view can use it:

```xml
<Style x:Key="Card" TargetType="Border">
    <Setter Property="Background" Value="{DynamicResource SurfaceBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="10"/>
</Style>
```

---

## 6. Component catalogue

### 6.1 Buttons

Three styles, one template:

| Style | Use | Normal | Hover | Pressed | Disabled |
|---|---|---|---|---|---|
| implicit `Button` (`BaseButton`) | secondary actions | surface / text / border | surface-alt + accent border | selection tint | 45% opacity |
| `PrimaryButton` | the main action | accent / white / accent | same template hover (surface-alt + accent) | selection tint | 45% opacity |
| `DangerButton` | destructive/stop | danger / white / danger | same template hover | selection tint | 45% opacity |

```xml
<Button Content="Discover"/>
<Button Content="Start" Style="{StaticResource PrimaryButton}"/>
<Button Content="Delete" Style="{StaticResource DangerButton}"/>
<Button Content="OK" Style="{StaticResource PrimaryButton}" IsDefault="True" MinWidth="90"/>
<Button Content="Cancel" IsCancel="True" MinWidth="90"/>
```

**Runtime semantic switch (start → stop):** the app keeps one primary button and changes its
colour when the operation is active.

```xml
<Button Content="{Binding StartStopText}" Command="{Binding ToggleShapingCommand}" MinWidth="112">
    <Button.Style>
        <Style TargetType="Button" BasedOn="{StaticResource PrimaryButton}">
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsRunning}" Value="True">
                    <Setter Property="Background" Value="{DynamicResource DangerBrush}"/>
                    <Setter Property="BorderBrush" Value="{DynamicResource DangerBrush}"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Button.Style>
</Button>
```

### 6.2 Segmented toggle

A two-state choice built from independent `ToggleButton`s (not a `RadioButton` group). The
selected one is accent-filled; the unselected one is dimmed.

```xml
<ToggleButton Content="Option A" Style="{StaticResource SegmentedToggle}"
              IsChecked="{Binding IsA, Mode=OneWay}"
              Command="{Binding ChooseCommand}" CommandParameter="A" Margin="0,0,4,0"/>
<ToggleButton Content="Option B" Style="{StaticResource SegmentedToggle}"
              IsChecked="{Binding IsB, Mode=OneWay}"
              Command="{Binding ChooseCommand}" CommandParameter="B"/>
```

### 6.3 TextBox

- Surface background, 1 px border, 5 px radius, `6,3` padding.
- Focus is communicated only by the border turning accent.
- Selection uses the accent brush.

```xml
<TextBox Text="{Binding Name, UpdateSourceTrigger=LostFocus}" MinWidth="140"/>
```

### 6.4 CheckBox

- 18 × 18 box, 4 px radius.
- Unchecked: surface background + border.
- Checked: accent background + white check path (2 px, rounded caps).
- Label sits 7 px to the right, vertically centred.
- Hover turns the border accent.

```xml
<CheckBox IsChecked="{Binding Enabled, Mode=TwoWay}" HorizontalAlignment="Center"/>
```

### 6.5 DataGrid family

The grid is the visual centre of the app. Its look comes from four implicit styles:

| Part | Specification |
|---|---|
| `DataGrid` | transparent background, no outer border, horizontal grid lines only, 34 px rows, single full-row selection, no add/delete/resize, no auto columns |
| `DataGridColumnHeader` | surface-alt background, dim text, semi-bold, `10,8` padding, 1 px bottom border |
| `DataGridCell` | transparent, no border, `10,4` padding, primary text even when selected |
| `DataGridRow` | transparent; hover = surface-alt; selected = selection tint |

```xml
<Border Style="{StaticResource Card}" ClipToBounds="True">
    <DataGrid ItemsSource="{Binding Rows}"
              SelectedItem="{Binding SelectedRow, Mode=TwoWay}"
              IsReadOnly="False" RowHeight="34" SelectionMode="Single">
        <DataGrid.Columns>
            <!-- column definitions -->
        </DataGrid.Columns>
    </DataGrid>
</Border>
```

**Column patterns worth reusing**

1. **Editable name + role badge**

```xml
<DataGridTemplateColumn Header="Name" Width="1.5*" MinWidth="110" CanUserSort="False">
    <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal" ToolTip="{Binding DisplayName}">
                <TextBlock Text="{Binding DisplayName}" TextTrimming="CharacterEllipsis"/>
                <TextBlock Text="{Binding RoleTag}" Foreground="{DynamicResource AccentBrush}"
                           FontWeight="SemiBold" Margin="6,0,0,0"/>
            </StackPanel>
        </DataTemplate>
    </DataGridTemplateColumn.CellTemplate>
    <DataGridTemplateColumn.CellEditingTemplate>
        <DataTemplate>
            <TextBox Text="{Binding DisplayName, UpdateSourceTrigger=LostFocus}" MinWidth="140"/>
        </DataTemplate>
    </DataGridTemplateColumn.CellEditingTemplate>
</DataGridTemplateColumn>
```

2. **Monospaced identifier**

```xml
<TextBlock Text="{Binding IpText}" FontFamily="Consolas"/>
```

3. **Direction-coloured metric**

```xml
<TextBlock Text="{Binding DownText}" Foreground="{DynamicResource ChartDownBrush}"
           FontWeight="SemiBold"/>
```

4. **Compact numeric editor**

```xml
<TextBox Text="{Binding CapDownText, UpdateSourceTrigger=LostFocus}"
         HorizontalContentAlignment="Center"/>
```

5. **Centred checkbox column**

```xml
<DataGridTemplateColumn Header="Block" Width="76" CanUserSort="False">
    <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
            <CheckBox IsChecked="{Binding Blocked, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                      IsEnabled="{Binding CanEditFlags}" HorizontalAlignment="Center"/>
        </DataTemplate>
    </DataGridTemplateColumn.CellTemplate>
</DataGridTemplateColumn>
```

6. **Two-line header with tooltip** (built in code-behind because `DataGridColumn` has no
   `HeaderToolTip`):

```csharp
private static TextBlock Header2(string first, string second, string? tooltip)
{
    var tb = new TextBlock();
    tb.Inlines.Add(new Run(first));
    tb.Inlines.Add(new LineBreak());
    tb.Inlines.Add(new Run(second));
    if (tooltip != null) tb.ToolTip = tooltip;
    return tb;
}
```

7. **Commit on Enter** (finishes a half-typed cell edit without a second click):

```csharp
private void OnGridKeyDown(object sender, KeyEventArgs e)
{
    if (e.Key != Key.Enter) return;
    Grid.CommitEdit(DataGridEditingUnit.Cell, true);
    Grid.CommitEdit(DataGridEditingUnit.Row, true);
    e.Handled = true;
}
```

### 6.6 Slim scrollbar

- 10 px wide, transparent track, thumb = border brush, 5 px radius, 1 px inset.
- No arrow buttons; the page-repeat buttons are invisible.
- **Vertical only in practice.** See [known limitations](#13-known-limitations-and-gotchas) for
  horizontal scrollbars.

### 6.7 Indeterminate progress bar

- 6 px tall, 3 px radius.
- Track = surface-alt, indicator = accent.
- A 96 px indicator slides from `-96` to `340` over 1.3 s, forever.
- Used exclusively inside the busy overlay.

```xml
<ProgressBar IsIndeterminate="True"/>
```

### 6.8 ToolTip

Surface-alt background, 1 px border, 5 px radius, `10,6` padding. Applied implicitly to every
tooltip in the app.

```xml
<Button Content="Discover" ToolTip="Scan the network for devices"/>
```

### 6.9 Card

The core grouping primitive: surface fill, 1 px border, 10 px radius, optional padding.
Used for the grid container, both chart panels, and the busy dialog.

```xml
<Border Style="{StaticResource Card}">
    <!-- content -->
</Border>
```

### 6.10 Toolbar

- Full-width surface strip with a 1 px bottom border.
- `12,9` padding.
- Left cluster: primary actions and segmented choices.
- Right cluster: utility toggles (units, theme, language) and About.
- Items are separated by `8`–`20` px margins, not dividers.

```xml
<Border Background="{DynamicResource SurfaceBrush}"
        BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1" Padding="12,9">
    <DockPanel>
        <StackPanel Orientation="Horizontal" DockPanel.Dock="Left">
            <Button Content="Discover" Margin="0,0,8,0"/>
            <Button Content="Start" Style="{StaticResource PrimaryButton}" MinWidth="112" Margin="0,0,16,0"/>
        </StackPanel>
        <StackPanel Orientation="Horizontal" DockPanel.Dock="Right" HorizontalAlignment="Right">
            <Button Content="Units" Margin="0,0,8,0"/>
            <Button Content="Dark"/>
        </StackPanel>
    </DockPanel>
</Border>
```

### 6.11 Warning banner

A collapsible full-width strip directly under the toolbar. Only visible when a problem exists.

```xml
<Border Background="{DynamicResource WarningBannerBrush}" Padding="14,8"
        Visibility="{Binding HasProblem, Converter={StaticResource BoolToVis}}">
    <TextBlock Text="{Binding ProblemText}"
               Foreground="{DynamicResource WarningTextBrush}" TextWrapping="Wrap"/>
</Border>
```

### 6.12 Status bar + state dot

- Surface strip with a 1 px top border, `14,8` padding, 10 px top margin.
- Left: 9 px dot + status text (ellipsis).
- Right: counters + a coloured badge in accent.
- Dot is dim when idle, success-green when running.

```xml
<Border Padding="14,8" Margin="0,10,0,0"
        Background="{DynamicResource SurfaceBrush}"
        BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,1,0,0">
    <DockPanel>
        <Ellipse Width="9" Height="9" Margin="0,0,8,0" VerticalAlignment="Center">
            <Ellipse.Style>
                <Style TargetType="Ellipse">
                    <Setter Property="Fill" Value="{DynamicResource TextDimBrush}"/>
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding IsRunning}" Value="True">
                            <Setter Property="Fill" Value="{DynamicResource SuccessBrush}"/>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </Ellipse.Style>
        </Ellipse>
        <TextBlock Text="{Binding StatusText}" VerticalAlignment="Center"
                   Foreground="{DynamicResource TextDimBrush}" TextTrimming="CharacterEllipsis"/>
    </DockPanel>
</Border>
```

### 6.13 Busy overlay (modal, in-window)

- Covers the entire window: `Grid.RowSpan` equal to all rows, `Panel.ZIndex="10"`.
- Background = `OverlayBrush` (dims dark theme; whites out light theme).
- Centred 340 px card with 24 px padding, message, and an indeterminate progress bar.
- No hit-test disabling is required because the overlay itself blocks input.

```xml
<Border Grid.RowSpan="5" Panel.ZIndex="10" Background="{DynamicResource OverlayBrush}"
        Visibility="{Binding IsBusy, Converter={StaticResource BoolToVis}}">
    <Border Style="{StaticResource Card}" Width="340" Padding="24"
            HorizontalAlignment="Center" VerticalAlignment="Center">
        <StackPanel>
            <TextBlock Text="{Binding BusyText}" HorizontalAlignment="Center" Margin="0,0,0,16"
                       Foreground="{DynamicResource TextBrush}" TextWrapping="Wrap" TextAlignment="Center"/>
            <ProgressBar IsIndeterminate="True"/>
        </StackPanel>
    </Border>
</Border>
```

### 6.14 Modal dialog pattern

The adapter picker is the reference dialog:

- 780 × 460, non-resizable, centred on owner, hidden from taskbar.
- Same window background/foreground/font tokens as the main window.
- 18 px outer margin.
- Prompt line, card-wrapped grid, right-aligned Cancel/OK row.
- OK uses `PrimaryButton`, `IsDefault`; Cancel uses `IsCancel`.
- OK is disabled until a row is selected.
- Double-click a row accepts the dialog.
- Column headers are set from code-behind for localization.

```xml
<Window ... Width="780" Height="460" ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Background="{DynamicResource BackgroundBrush}"
        Foreground="{DynamicResource TextBrush}"
        FontFamily="Segoe UI Variable Text, Segoe UI" FontSize="13" FontWeight="Medium"
        UseLayoutRounding="True" SnapsToDevicePixels="True">
    <Grid Margin="18">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        <!-- prompt, card-wrapped grid -->
        <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,14,0,0">
            <Button Content="Cancel" MinWidth="90" Margin="0,0,8,0" IsCancel="True"/>
            <Button Content="OK" MinWidth="90" Style="{StaticResource PrimaryButton}" IsDefault="True"/>
        </StackPanel>
    </Grid>
</Window>
```

### 6.15 Window base properties

Every window repeats this baseline:

```xml
Background="{DynamicResource BackgroundBrush}"
Foreground="{DynamicResource TextBrush}"
FontFamily="Segoe UI Variable Text, Segoe UI"
FontSize="13"
FontWeight="Medium"
UseLayoutRounding="True"
SnapsToDevicePixels="True"
TextOptions.TextFormattingMode="Ideal"
```

---

## 7. Custom chart control

### 7.1 What it is

`BandwidthChartControl` is a dependency-free, real-time, two-series area chart built directly
on `FrameworkElement.OnRender`. It is the only bespoke visual control in the project.

**Behaviour**

- Keeps a rolling window of **120 samples** per series (~2 minutes at 1 Hz).
- Each new sample is appended; the oldest is discarded when full.
- Auto-scales to a "nice" ceiling (1, 2, 2.5, 5, 10 × power of ten).
- Draws four horizontal grid bands and scale labels.
- Draws a gradient-filled area plus a 1.7 px rounded line for each series.
- Shows current values as `↓ value    ↑ value` in the top-right.
- Slides the newest sample in over roughly 0.5 s using `CompositionTarget.Rendering`.
- Reads theme brushes at paint time, so it recolours when the theme changes.
- Uses DPI-aware `FormattedText` for labels.

**Public surface**

| Member | Type | Purpose |
|---|---|---|
| `Title` | `string` dependency property | Card title, also affects render |
| `UnitMode` | `UnitMode` plain property | Bytes vs bits formatting |
| `PushSample(double down, double up)` | method | Add one sample (bytes/sec) |
| `ClearData()` | method | Reset the series |

### 7.2 Integration

```xml
xmlns:controls="clr-namespace:YourApp.Controls"

<Border Style="{StaticResource Card}" Padding="10">
    <controls:BandwidthChartControl x:Name="MainChart" Title="Total bandwidth"/>
</Border>
```

```csharp
// on each stats tick
MainChart.PushSample(downBytesPerSecond, upBytesPerSecond);

// when the unit setting changes
MainChart.UnitMode = UnitMode.Bits;

// after a theme swap
MainChart.InvalidateVisual();
```

> `UnitMode` is a plain CLR property in the original, not a dependency property, so it cannot be
> data-bound. If binding is required, wrap it in a `DependencyProperty` (see recipe 12.4).

### 7.3 Full source

The original file imports `SelfishNetV2.Services` for `UnitMode` and `SpeedFormatter`. Replace
that namespace with your own (or use the shim in §7.4).

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SelfishNetV2.Services;

namespace SelfishNetV2.App.Controls;

/// <summary>
/// Lightweight real-time bandwidth chart. Keeps a rolling 120-sample window per
/// series (2 minutes at the 1 Hz stats cadence), auto-scales to a "nice" ceiling and
/// smoothly scrolls each new sample in over ~0.5 s (CompositionTarget.Rendering).
/// Zero external charting dependencies; colors come from the active theme dictionary.
/// </summary>
public sealed class BandwidthChartControl : FrameworkElement
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(BandwidthChartControl),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    private const int Capacity = 120;
    private readonly double[] _down = new double[Capacity];
    private readonly double[] _up = new double[Capacity];
    private int _count;
    private double _scroll;          // 1 → 0 between samples
    private bool _renderingHooked;
    private UnitMode _unitMode = UnitMode.Bytes;

    /// <summary>Unit system for axis + legend labels (follows the app's Units toggle).</summary>
    public UnitMode UnitMode
    {
        get => _unitMode;
        set
        {
            if (_unitMode == value) return;
            _unitMode = value;
            InvalidateVisual();
        }
    }

    private static readonly FontFamily ChartFont = new("Segoe UI Variable Text, Segoe UI");
    private static readonly Typeface Face = new(ChartFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public BandwidthChartControl()
    {
        ClipToBounds = true;
        Loaded += (_, _) => HookRendering(true);
        Unloaded += (_, _) => HookRendering(false);
    }

    private void HookRendering(bool on)
    {
        if (on == _renderingHooked) return;
        _renderingHooked = on;
        if (on) CompositionTarget.Rendering += OnFrame;
        else CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_scroll <= 0) return;
        _scroll = Math.Max(0, _scroll - 1.0 / 30.0);
        InvalidateVisual();
    }

    public void PushSample(double downBytesPerSec, double upBytesPerSec)
    {
        if (_count < Capacity)
        {
            _down[_count] = downBytesPerSec;
            _up[_count] = upBytesPerSec;
            _count++;
        }
        else
        {
            Array.Copy(_down, 1, _down, 0, Capacity - 1);
            Array.Copy(_up, 1, _up, 0, Capacity - 1);
            _down[^1] = downBytesPerSec;
            _up[^1] = upBytesPerSec;
        }
        _scroll = 1.0;
        InvalidateVisual();
    }

    public void ClearData()
    {
        _count = 0;
        _scroll = 0;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 40 || h < 40) return;

        Brush textDim = Res("TextDimBrush", Brushes.Gray);
        Brush downBrush = Res("ChartDownBrush", Brushes.DodgerBlue);
        Brush upBrush = Res("ChartUpBrush", Brushes.LimeGreen);

        const double padL = 8, padR = 8, padT = 28, padB = 12;
        double cw = w - padL - padR;
        double ch = h - padT - padB;

        DrawText(dc, Title, 2, 2, 12, textDim, FontWeights.SemiBold);

        // ---- auto-scale ----
        double max = 1;
        for (int i = 0; i < _count; i++)
        {
            if (_down[i] > max) max = _down[i];
            if (_up[i] > max) max = _up[i];
        }
        double scaleMax = NiceCeiling(max);

        // ---- horizontal grid + scale labels ----
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x26, 0x88, 0x88, 0x88)), 1);
        gridPen.Freeze();
        for (int i = 0; i <= 4; i++)
        {
            double y = padT + ch - ch * i / 4.0;
            dc.DrawLine(gridPen, new Point(padL, y), new Point(padL + cw, y));
            if (i > 0)
                DrawText(dc, SpeedFormatter.FormatBytes(scaleMax * i / 4.0, _unitMode),
                    padL + 2, y - 14, 9, textDim, FontWeights.Normal);
        }

        // ---- current values (top-right legend) ----
        double lastDown = _count > 0 ? _down[_count - 1] : 0;
        double lastUp = _count > 0 ? _up[_count - 1] : 0;
        FormattedText legend = MakeText(
            $"\u2193 {SpeedFormatter.FormatBytes(lastDown, _unitMode)}    \u2191 {SpeedFormatter.FormatBytes(lastUp, _unitMode)}",
            11, textDim, FontWeights.SemiBold);
        dc.DrawText(legend, new Point(w - legend.Width - 4, 3));

        if (_count < 2)
        {
            dc.DrawLine(gridPen, new Point(padL, padT + ch), new Point(padL + cw, padT + ch));
            return;
        }

        // ---- series ----
        double step = cw / (Capacity - 1);
        double offset = _scroll * step;
        DrawSeries(dc, _down, downBrush, padL, padT, cw, ch, step, offset);
        DrawSeries(dc, _up, upBrush, padL, padT, cw, ch, step, offset);
    }

    private void DrawSeries(DrawingContext dc, double[] values, Brush brush,
        double padL, double padT, double cw, double ch, double step, double offset)
    {
        int n = _count;
        // Points are right-anchored; the newest sample slides in from one step beyond
        // the right edge (clipped), producing the smooth scroll.
        double firstX = padL + cw - (n - 1) * step + offset;
        double bottom = padT + ch;
        double scaleMax = seriesScaleMax();
        double scaleY(double v) => bottom - (v / scaleMax) * (ch - 2);

        // filled area under the curve
        var fillGeo = new StreamGeometry();
        using (var g = fillGeo.Open())
        {
            g.BeginFigure(new Point(firstX, bottom), true, true);
            for (int i = 0; i < n; i++)
                g.LineTo(new Point(firstX + i * step, scaleY(values[i])), true, false);
            g.LineTo(new Point(firstX + (n - 1) * step, bottom), true, false);
        }
        fillGeo.Freeze();

        Color baseColor = brush is SolidColorBrush sc ? sc.Color : Colors.DodgerBlue;
        var fill = new LinearGradientBrush(
            Color.FromArgb(0x3C, baseColor.R, baseColor.G, baseColor.B),
            Color.FromArgb(0x02, baseColor.R, baseColor.G, baseColor.B),
            new Point(0, 0), new Point(0, 1));
        fill.Freeze();
        dc.DrawGeometry(fill, null, fillGeo);

        // stroked line on top
        var lineGeo = new StreamGeometry();
        using (var g = lineGeo.Open())
        {
            g.BeginFigure(new Point(firstX, scaleY(values[0])), false, false);
            for (int i = 1; i < n; i++)
                g.LineTo(new Point(firstX + i * step, scaleY(values[i])), true, false);
        }
        lineGeo.Freeze();
        var pen = new Pen(brush, 1.7) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, lineGeo);

        double seriesScaleMax()
        {
            double m = 1;
            for (int i = 0; i < n; i++) if (values[i] > m) m = values[i];
            return NiceCeiling(m);
        }
    }

    private static double NiceCeiling(double v)
    {
        if (v <= 1) return 1;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(v)));
        double n = v / magnitude;
        double nice = n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10;
        return nice * magnitude;
    }

    private void DrawText(DrawingContext dc, string text, double x, double y, double size, Brush brush, FontWeight weight) =>
        dc.DrawText(MakeText(text, size, brush, weight), new Point(x, y));

    private FormattedText MakeText(string text, double size, Brush brush, FontWeight weight) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Face.FontFamily, FontStyles.Normal, weight, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
}
```

### 7.4 Portable formatter shim

The chart needs exactly one external function and one enum. Drop this into the target project
if it does not already have an equivalent:

```csharp
namespace YourApp.Controls;

public enum UnitMode { Bytes, Bits }

public static class SpeedFormatter
{
    public static string FormatBytes(double bytesPerSec, UnitMode mode)
    {
        if (mode == UnitMode.Bytes)
        {
            if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B/s";
            double kb = bytesPerSec / 1024;
            if (kb < 1024) return $"{kb:F1} KByte/s";
            double mb = kb / 1024;
            if (mb < 1024) return $"{mb:F1} MByte/s";
            return $"{mb / 1024:F2} GByte/s";
        }
        else
        {
            double bps = bytesPerSec * 8;
            if (bps < 1000) return $"{bps:F0} b/s";
            double kb = bps / 1000;
            if (kb < 1000) return $"{kb:F1} KBit/s";
            double mb = kb / 1000;
            if (mb < 1000) return $"{mb:F1} MBit/s";
            return $"{mb / 1000:F2} GBit/s";
        }
    }
}
```

Then change the chart's `using SelfishNetV2.Services;` to `using YourApp.Controls;` (or the
namespace where the shim lives).

### 7.5 Rendering details you may want to tune

| Constant | Where | Effect |
|---|---|---|
| `Capacity = 120` | field | Window length; raise for longer history |
| `1.0 / 30.0` | `OnFrame` | Scroll speed per render frame |
| `padL/padR/padT/padB = 8/8/28/12` | `OnRender` | Inner plot margins |
| grid `for i <= 4` | `OnRender` | Number of horizontal bands |
| `0x26, 0x88, 0x88, 0x88` | grid pen | Grid line colour; currently theme-independent |
| `0x3C` / `0x02` | area gradient | Fill strength at top/bottom |
| `1.7` | line pen | Stroke thickness |
| `NiceCeiling` | method | Axis rounding strategy |
| `9 / 11 / 12` | text sizes | Axis / legend / title |

---

## 8. Theme engine source

### 8.1 `Theming/ThemeManager.cs`

```csharp
using System.Windows;
using System.Windows.Interop;

namespace YourApp.Theming;

public enum AppTheme { Dark, Light }

/// <summary>Swaps the active palette ResourceDictionary; every themed brush is a
/// DynamicResource so the whole UI recolors instantly. Also flips the Win32 title bar.</summary>
public static class ThemeManager
{
    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    /// <summary>Component-relative pack URI — resolves inside THIS assembly regardless of
    /// which assembly is the entry point (the application-relative form silently breaks
    /// when the shell is hosted by another exe).</summary>
    private static Uri ThemeUri(AppTheme theme)
    {
        string assembly = typeof(ThemeManager).Assembly.GetName().Name!;
        return new Uri($"pack://application:,,,/{assembly};component/Themes/{theme}.xaml", UriKind.Absolute);
    }

    public static void Apply(AppTheme theme)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        for (int i = 0; i < merged.Count; i++)
        {
            string src = merged[i].Source?.ToString() ?? "";
            if (src.Contains("Themes/Dark.xaml") || src.Contains("Themes/Light.xaml"))
            {
                merged[i] = new ResourceDictionary { Source = ThemeUri(theme) };
                Current = theme;
                foreach (Window w in Application.Current.Windows)
                    TitleBarHelper.Apply(w, theme);
                return;
            }
        }
        // first call: no theme dictionary yet → insert at the front
        merged.Insert(0, new ResourceDictionary { Source = ThemeUri(theme) });
        Current = theme;
    }
}
```

### 8.2 DWM title bar helper

```csharp
public static class TitleBarHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;          // Windows 10 1903+ / Windows 11
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_1809 = 19;     // Windows 10 1809

    public static void Apply(Window window, AppTheme theme)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int useDark = theme == AppTheme.Dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_1809, ref useDark, sizeof(int));
        }
        catch { /* below 1809: keep the default (light) chrome */ }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
```

### 8.3 Wiring in the application

At startup, before showing any window:

```csharp
var settings = SettingsStore.Load();
ThemeManager.Apply(settings.Theme == "Light" ? AppTheme.Light : AppTheme.Dark);
```

In each window:

```csharp
SourceInitialized += (_, _) => TitleBarHelper.Apply(this, ThemeManager.Current);
```

Toggle command:

```csharp
private void ToggleTheme()
{
    Theme = Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
    _settings.Theme = Theme.ToString();
    _settings.Save();

    ThemeManager.Apply(Theme);

    OnPropertyChanged(nameof(Theme));
    OnPropertyChanged(nameof(ThemeButtonText));
    ThemeChanged?.Invoke();          // custom-drawn controls repaint
}
```

Persistence model (only one UI-related field):

```csharp
public sealed class SettingsStore
{
    public string Theme { get; set; } = "Dark";
    // ... other non-visual settings
}
```

---

## 9. App shell blueprint

The main window is a five-row grid. This layout is domain-neutral and can be reused for any
dashboard/utility app.

```
┌──────────────────────────────────────────────────────────────┐
│ Row 0  TOOLBAR (Auto)                                        │
│        left: primary actions + mode toggle                   │
│        right: utility toggles + About                        │
├──────────────────────────────────────────────────────────────┤
│ Row 1  WARNING BANNER (Auto, collapsible)                    │
├──────────────────────────────────────────────────────────────┤
│ Row 2  PRIMARY CONTENT CARD (Star)                           │
│        card margin 12,12,12,0; clip to bounds                │
├──────────────────────────────────────────────────────────────┤
│ Row 3  SECONDARY PANEL / CHARTS (Auto, 188 px)               │
│        two equal cards separated by 12 px                    │
├──────────────────────────────────────────────────────────────┤
│ Row 4  STATUS BAR (Auto)                                     │
└──────────────────────────────────────────────────────────────┘

Overlay: Grid.RowSpan=5, Panel.ZIndex=10, visible while busy
```

Skeleton:

```xml
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>   <!-- toolbar -->
        <RowDefinition Height="Auto"/>   <!-- banner -->
        <RowDefinition Height="*"/>      <!-- main content -->
        <RowDefinition Height="Auto"/>   <!-- charts -->
        <RowDefinition Height="Auto"/>   <!-- status -->
    </Grid.RowDefinitions>

    <!-- toolbar -->
    <!-- banner -->
    <Border Grid.Row="2" Style="{StaticResource Card}" Margin="12,12,12,0" ClipToBounds="True">
        <!-- primary content -->
    </Border>

    <Grid Grid.Row="3" Margin="12,12,12,0" Height="188">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="12"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>
        <Border Grid.Column="0" Style="{StaticResource Card}" Padding="10"><!-- chart --></Border>
        <Border Grid.Column="2" Style="{StaticResource Card}" Padding="10"><!-- chart --></Border>
    </Grid>

    <!-- status bar -->
    <!-- busy overlay -->
</Grid>
```

**Why it works**

- The toolbar and status bar anchor the frame, so content changes never move them.
- The star row absorbs resizing; every other row keeps its intrinsic height.
- Cards give the content a consistent inset without complex nested layouts.
- The overlay covers the whole shell, so busy states never leave interactive controls visible.
- Minimum size (880 × 470) is chosen so the two chart cards and the grid stay usable.

---

## 10. Porting to another project

### 10.1 Prerequisites

- .NET 8 SDK (or newer) on Windows.
- A WPF project: `<UseWPF>true</UseWPF>`, `<TargetFramework>net8.0-windows</TargetFramework>`.
- PerMonitorV2 DPI awareness in `app.manifest`.

### 10.2 Step-by-step

1. **Create the folders**

   ```
   Themes/     Dark.xaml, Light.xaml, Controls.xaml
   Theming/    ThemeManager.cs
   Controls/   BandwidthChartControl.cs   (optional)
   ```

2. **Copy the three dictionaries** from §5 verbatim.

3. **Copy `ThemeManager.cs`** from §8 and change the namespace to your app's.

4. **Merge in `App.xaml`** exactly as in §5.4. Keep `Controls.xaml` after the palette.

5. **Set the window baseline** from §6.15 on every window.

6. **Apply the persisted theme at startup**, before the first window is shown (§8.3).

7. **Add the title-bar hook** in each window's `SourceInitialized` event (§8.3).

8. **Add a theme toggle** (button + command) and persist `"Dark"` / `"Light"`.

9. **Wrap primary content in cards** and build the five-row shell from §9.

10. **Add the chart only if needed.** Copy the control, add the shim from §7.4, then feed it
    samples on a UI-thread timer. Call `InvalidateVisual()` after a theme change.

11. **Add the DPI manifest**:

    ```xml
    <application xmlns="urn:schemas-microsoft-com:asm.v3">
      <windowsSettings>
        <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
        <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      </windowsSettings>
    </application>
    ```

12. **Run the acceptance checklist** (§10.4).

### 10.3 Namespace and name changes

| Original | Replace with |
|---|---|
| `SelfishNetV2.App.Theming` | `YourApp.Theming` |
| `SelfishNetV2.App.Controls` | `YourApp.Controls` |
| `SelfishNetV2.Services` in the chart | your formatter namespace |
| `SelfishNetV2.App` in `App.xaml` | your app class |
| `Themes/Dark.xaml` strings in `ThemeManager` | your file names, if renamed |
| `AppTheme`, `AppTheme.Dark` | keep or rename; no external contract |

### 10.4 Acceptance checklist

- [ ] App starts in dark theme with no black/transparent text.
- [ ] Toggling to light updates every surface, text, border, grid, tooltip, and chart instantly.
- [ ] No element keeps a hard-coded colour after the switch (search for `#` in XAML outside `Themes/`).
- [ ] Buttons show hover, pressed, and disabled states.
- [ ] TextBox focus border turns accent.
- [ ] CheckBox check mark is visible in both themes.
- [ ] DataGrid text stays readable when a row is selected in dark theme.
- [ ] The OS title bar turns dark/light with the app.
- [ ] All windows render crisply at 100 %, 150 %, and 200 % scaling.
- [ ] Keyboard tab order is sane; default/cancel buttons respond to Enter/Escape.
- [ ] Busy overlay blocks the entire window.
- [ ] Chart recolours after a theme switch and after a unit change.
- [ ] No missing-resource warnings in the Visual Studio output window.

### 10.5 Minimal file count

If you do not need the chart or the DWM title bar, the reusable visual core is only:

```
App.xaml
Themes/Dark.xaml
Themes/Light.xaml
Themes/Controls.xaml
Theming/ThemeManager.cs
```

---

## 11. Adapting outside this field

This visual system is not tied to networking. To reuse it for a CRM, finance dashboard,
inventory tool, log viewer, etc.:

### 11.1 Keep

- The palette structure and semantic brush keys.
- The control dictionary unchanged.
- The five-row shell (toolbar → banner → content → secondary panel → status).
- The card + grid + status-dot language.
- The theme manager and instant swap behaviour.

### 11.2 Replace

| App-specific thing | Neutral replacement |
|---|---|
| `ChartDownBrush` / `ChartUpBrush` | `SeriesPrimaryBrush` / `SeriesSecondaryBrush` |
| Download/upload arrows | any two-series legend, or hide the legend |
| `SpeedFormatter` in the chart | currency, count, percentage, or duration formatter |
| IP/MAC columns | IDs, dates, amounts, statuses |
| Engine segmented toggle | any two-mode switch (list/grid, simple/advanced, etc.) |
| Per-device flags | per-row toggles relevant to the domain |
| `RoleTag` accent text | status badge or category label |
| Warning banner text | validation/connection/permission warnings |
| `Consolas` columns | any identifier, SKU, account number, or timestamp |

### 11.3 Rebrand

Only edit the two palette files. Suggested procedure:

1. Choose a base neutral ramp (background → surface → surface-alt → border) with roughly the
   same contrast steps as the originals.
2. Pick one accent and one hover shade. Use it for selection, primary buttons, links, and focus.
3. Pick success/danger hues that remain distinguishable for the most common colour-vision
   deficiencies (blue/green vs orange/red is safer than red/green alone).
4. Keep `SelectionBrush` at 18–28 % opacity so selected rows remain readable.
5. Re-check white-on-accent contrast (see §13.4).
6. Do **not** add per-component colours; add semantic keys instead.

### 11.4 If the target is not WPF

| Concept | WPF | Avalonia | WinUI 3 | Web (CSS) |
|---|---|---|---|---|
| Palette swap | `ResourceDictionary` merge | `ResourceDictionary` merge | `ThemeResource` + `ResourceDictionary` | CSS custom properties on `:root` |
| Dynamic reference | `DynamicResource` | `DynamicResource` | `ThemeResource` | `var(--token)` |
| Control template | `ControlTemplate` | `ControlTemplate` | `ControlTemplate` | component CSS |
| Custom drawing | `DrawingContext` | `DrawingContext` | `Canvas` / Win2D | `<canvas>` / SVG |
| Frame tick | `CompositionTarget.Rendering` | `CompositionTarget.Rendering` | `CompositionTarget.Rendering` | `requestAnimationFrame` |
| Dark title bar | DWM P/Invoke | DWM P/Invoke | `AppWindowTitleBar` | browser chrome (not controllable) |

The token names, spacing, radii, and component recipes transfer directly; only the styling
syntax changes.

---

## 12. Extension recipes

### 12.1 Add a third theme (e.g. high contrast)

1. Create `Themes/HighContrast.xaml` with the **same keys** as Dark/Light.
2. Extend `AppTheme` with `HighContrast`.
3. Make `ThemeUri` resolve the new file name.
4. Update the `Contains(...)` match in `ThemeManager.Apply`.
5. Add the choice to the settings string and the toggle UI.
6. Add an acceptance check for every key.

### 12.2 Add a palette token

1. Add the `Color` and `SolidColorBrush` to **both** palette files.
2. Use `{DynamicResource NewBrush}` in controls.
3. Add it to Appendix A.
4. Verify both themes visually.

### 12.3 Add an icon button

```xml
<Button Width="32" Height="32" Padding="0" ToolTip="Settings">
    <Path Data="M ..." Width="16" Height="16" Stretch="Uniform"
          Stroke="{DynamicResource TextBrush}" StrokeThickness="1.75"
          StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
</Button>
```

Keep the 16 px box and 1.75 px stroke to match the checkbox check mark.

### 12.4 Make `UnitMode` bindable

```csharp
public static readonly DependencyProperty UnitModeProperty =
    DependencyProperty.Register(nameof(UnitMode), typeof(UnitMode), typeof(BandwidthChartControl),
        new FrameworkPropertyMetadata(UnitMode.Bytes, FrameworkPropertyMetadataOptions.AffectsRender));

public UnitMode UnitMode
{
    get => (UnitMode)GetValue(UnitModeProperty);
    set => SetValue(UnitModeProperty, value);
}
```

Then bind:

```xml
<controls:BandwidthChartControl Title="Total" UnitMode="{Binding Units}"/>
```

### 12.5 Add a visible keyboard focus ring

The current templates do not define one. Add a focus border or an outer glow-like ring
(no actual blur) to `BaseButton`, `SegmentedToggle`, `TextBox`, and `CheckBox`:

```xml
<Trigger Property="IsKeyboardFocused" Value="True">
    <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}"/>
    <Setter Property="BorderThickness" Value="2"/>
</Trigger>
```

For a non-shifting ring, wrap the template border in an outer `Border` with 2 px padding and
change only its `BorderBrush`.

### 12.6 Add a horizontal scrollbar style

The current `ScrollBar` template ignores `Orientation`. Add an orientation trigger or a
separate `Style` keyed by `{x:Static ScrollBar.HorizontalScrollBarStyleKey}` if you expect
horizontal scrolling. At minimum, set `Height="10"` and use `ScrollBar.PageLeftCommand` /
`PageRightCommand` with a horizontal `Track`.

### 12.7 Add a sidebar navigation layout

Replace the toolbar's left cluster with a fixed-width column:

```
Grid
├─ Column 0: 220 px navigation card (surface, right border)
└─ Column 1: existing five-row shell
```

Use the same card surface and 1 px border; highlight the active item with `SelectionBrush` and
an accent left border, not with a filled accent block (keeps the hierarchy calm).

### 12.8 Add a modal confirmation dialog

Reuse §6.14 verbatim with:

- 420–520 px width,
- warning icon optional (there is no icon set; use a coloured dot or the warning text colour),
- destructive action as `DangerButton`, safe action as the implicit button,
- `IsDefault` / `IsCancel` preserved.

---

## 13. Known limitations and gotchas

### 13.1 Platform

- **Windows-only.** WPF does not run on macOS/Linux. For cross-platform, port the tokens and
  layouts to Avalonia (the closest match).
- **DWM title bar is best effort.** Windows 10 1607–1803 falls back to the standard light
  chrome; nothing breaks.
- **WPF trimming is unsupported.** Do not enable `PublishTrimmed` for this UI.

### 13.2 Theming

- A missing key in one palette produces invisible text or a black element at runtime, with no
  compile error. Keep the two palettes key-for-key identical.
- `ThemeManager` identifies palettes by **string match on the source path**. Renaming the files
  without updating the match silently disables swapping.
- `ThemeManager.Apply` only calls `TitleBarHelper` on the replacement path. New windows must
  hook `SourceInitialized` themselves.
- `App.xaml` hard-codes `Dark.xaml` as the design-time/default palette. If the persisted theme is
  light, there is a brief dark-first resource state before `Apply` runs. Apply it before
  `window.Show()` to avoid a visible flash.
- Palette brushes are not frozen. That is intentional for theme swapping, but do not expect
  cross-thread brush sharing.
- `StaticResource` inside a `ControlTemplate` is resolved once. Use `DynamicResource` for any
  value that must change with the theme.

### 13.3 Controls

- **No focus visuals** are defined for buttons, toggles, checkboxes, or the grid. Keyboard users
  can tab, but the focus location may be hard to see. See recipe 12.5.
- **CheckBox has no disabled state.** Disabling it changes hit-testing but not opacity; add an
  `IsEnabled=False` trigger if you rely on it.
- **ScrollBar is effectively vertical-only.** The template ignores `Orientation` and fixes
  `Width=10`.
- **Toolbar/status use margin spacing**, not separators. Adding many items requires manual
  spacing discipline.
- **DataGrid selection text** is explicitly reset to `TextBrush`; without that line, dark-theme
  selected rows render with unreadable system-default text.
- **Column headers built in code-behind** do not participate in XAML styling. Reuse
  `Header`/`Header2` helpers and keep font/foreground from the implicit `DataGridColumnHeader`
  style.
- **The chart's grid colour is hard-coded**, not themed. It is a neutral 15 % gray that works on
  both palettes; change the constant if you rebrand.
- **The chart has no automation peer**, no keyboard interaction, and no screen-reader summary.
  Provide an accessible textual summary elsewhere if the chart is essential.
- **`UnitMode` is not a dependency property** in the original, so it cannot be bound. See 12.4.

### 13.4 Accessibility

This is a compact, dark-first desktop UI, not a WCAG-certified system. Known risks:

- White text on the dark accent `#4C8DFF` is roughly **3.2:1**, below the 4.5:1 target for
  normal text. For stricter accessibility, darken the dark-theme accent or use dark text on
  primary buttons.
- White text on dark danger `#F85149` is roughly **3.4:1**, also below 4.5:1.
- Colour carries direction meaning in the chart and metrics; the `↓`/`↑` glyphs provide a
  non-colour cue, but row-level download/upload columns rely on colour plus column position.
- No high-contrast / system-theme mode is implemented.
- No visible focus ring (see above).
- Small axis labels (9 px) are legible on desktop at 100 % but may be too small at a distance
  or for low-vision users.

If accessibility is a requirement, add: a focus ring, a high-contrast palette, contrast-safe
accent variants, a chart data table, and `AutomationProperties.Name` on icon-only controls.

### 13.5 Visual consistency

- `Card` is defined in `MainWindow.xaml`, not in a shared dictionary. Other windows must
  duplicate it or move it (recommended: move it).
- `AccentHoverBrush` is defined but not used by the current templates. It is reserved for
  future hover work; do not assume a hover state uses it.
- Buttons use the same hover treatment for primary/danger (surface-alt + accent border), which
  can look inconsistent on a filled danger button. If you care, add style-specific hover
  triggers.
- The light-theme busy overlay is white at 75 %, which can wash out the underlying content
  more than the dark overlay does. This is intentional but worth reviewing on large monitors.

---

## 14. Source map and maintenance

### 14.1 Where each part came from

| This document | Source file | Notes |
|---|---|---|
| Tech inventory | `src/SelfishNetV2.App/SelfishNetV2.App.csproj` | .NET 8, WPF, no UI packages |
| Palette — dark | `Themes/Dark.xaml` | Verbatim |
| Palette — light | `Themes/Light.xaml` | Verbatim |
| Control styles | `Themes/Controls.xaml` | Verbatim |
| Theme engine | `Theming/ThemeManager.cs` | Namespace changed in doc only |
| Chart | `Controls/BandwidthChartControl.cs` | Verbatim; external formatter shim added |
| Shell layout | `MainWindow.xaml` | Condensed blueprint |
| Dialog pattern | `Views/AdapterWindow.xaml` | Condensed blueprint |
| Chart/theme wiring | `MainWindow.xaml.cs`, `ViewModels/MainViewModel.cs` | Summarised |
| Startup order | `App.xaml.cs` | Summarised |
| DPI | `app.manifest` | PerMonitorV2 |
| Historical WinForms theme | `prototype/.../UI/Win11Theme.cs` | Appendix B |

### 14.2 How to update this document

1. Change the app source first.
2. Mirror the change here in the matching section.
3. Keep the three dictionary blocks byte-identical to the source files.
4. Update the source map if a file moves.
5. Bump the document version and add a changelog row.
6. Re-run the acceptance checklist in §10.4.

### 14.3 Changelog

| Version | Date | Change |
|---|---|---|
| 1.0.0 | 2026-10-19 | Initial extraction from SelfishNet v2 WPF shell |

### 14.4 Suggested versioning

- **Patch** — wording, comments, source-map fix.
- **Minor** — new component recipe, new appendix, non-breaking token addition.
- **Major** — palette structure change, control template redesign, theme-engine contract change.

---

## Appendix A — resource key index

### Colours

`BackgroundColor`, `SurfaceColor`, `SurfaceAltColor`, `BorderColor`, `TextColor`,
`TextDimColor`, `AccentColor`, `AccentHoverColor`, `SuccessColor`, `DangerColor`,
`ChartDownColor`, `ChartUpColor`

### Brushes

`BackgroundBrush`, `SurfaceBrush`, `SurfaceAltBrush`, `BorderBrush`, `TextBrush`,
`TextDimBrush`, `AccentBrush`, `AccentHoverBrush`, `SuccessBrush`, `DangerBrush`,
`ChartDownBrush`, `ChartUpBrush`, `SelectionBrush`, `OverlayBrush`, `WarningBannerBrush`,
`WarningTextBrush`

### Styles

`BaseButton`, `PrimaryButton`, `DangerButton`, `SegmentedToggle`, `Card`
(plus implicit styles for `Button`, `TextBox`, `CheckBox`, `DataGrid`, `DataGridRow`,
`DataGridCell`, `DataGridColumnHeader`, `ScrollBar`, `ProgressBar`, `ToolTip`)

### Other

`BoolToVis` (`BooleanToVisibilityConverter`)

---

## Appendix B — historical WinForms treatment

Before the WPF rebuild, the prototype used a light, WinUI-inspired WinForms theme
(`prototype/SelfishNetV2.Test/UI/Win11Theme.cs`). It is documented here because its tokens and
DWM technique are independently reusable.

### B.1 Tokens

| Token | Value | Use |
|---|---|---|
| Accent | `#0078D4` | Windows 11 system accent |
| Grid line | `#EDEBE9` | DataGridView horizontal lines |
| Selection | `#CCE8FF` | Selected row background |
| Surface alt | `#F3F3F3` | Status strip |
| Text | `#1B1B1B` | Primary text |
| Window | `#FFFFFF` | Form background |

### B.2 Characteristics

- Light-only; no dark palette.
- Mica backdrop + rounded corners via DWM:
  - `DWMWA_WINDOW_CORNER_PREFERENCE = 33`, value `2` (`DWMWCP_ROUND`)
  - `DWMWA_SYSTEMBACKDROP_TYPE = 38`, value `2` (`DWMSBT_MAINWINDOW`)
  - Only attempted on Windows 11 (build 22000+); silently skipped otherwise.
- Font preference chain: `Segoe UI Variable Text` → `Segoe UI Variable` → `Segoe UI` →
  `SystemFonts.MessageBoxFont` → `SystemFonts.DefaultFont` → `Control.DefaultFont`.
- DataGridView: no border, single horizontal cell borders, 30 px rows, 34 px headers,
  bold left-aligned headers, 8 px left padding.
- Toolbar: white, hidden grip, `6,4,6,4` padding, 2 px item margins.

### B.3 Why it was superseded

The WinForms theme could not deliver the required dark mode, per-control templating, vector
scaling, or custom chart rendering with the same effort as WPF. The WPF system in this document
is the current and recommended base.

---

*End of document.*
