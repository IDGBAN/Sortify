# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## What this is

Sortify is a Windows desktop (WPF, .NET 8) app that reads a user's exported Spotify streaming
history (JSON) and turns it into browsable statistics, charts and insights. It ships as a single
portable self-contained `.exe`; there is no server component and no network calls — everything
runs and is stored locally (`%LOCALAPPDATA%\Sortify` holds the settings file, the parsed-history
cache and an error log).

## Commands

```bash
# Run the app
dotnet run --project Sortify/Sortify.csproj

# Build
dotnet build

# Run the full test suite
dotnet test

# Run a single test class or method (filter by fully-qualified name substring)
dotnet test --filter "FullyQualifiedName~AnalysisEngineTests"
dotnet test --filter "FullyQualifiedName~AnalysisEngineTests.SomeMethod"

# Publish a self-contained single-file build (64-bit or 32-bit)
dotnet publish Sortify/Sortify.csproj -c Release -r win-x64
dotnet publish Sortify/Sortify.csproj -c Release -r win-x86
```

The published exe lands in `Sortify/bin/Release/net8.0-windows/<rid>/publish/`. CI (`.github/workflows/ci.yml`)
runs `dotnet restore`, `dotnet format --verify-no-changes`, `dotnet build -c Debug`,
`dotnet test -c Debug` and a win-x64 Release publish on `windows-latest` for pushes to
`main`/`update-*` and all PRs.

Formatting/style rules (naming, brace placement, `var` usage, file-scoped namespaces) live in
`.editorconfig`. CI runs `dotnet format --verify-no-changes`; run `dotnet format` locally to fix
whatever it flags.

## Architecture

Two projects: `Sortify` (the WPF app) and `Sortify.Tests` (xUnit; `InternalsVisibleTo` is set so
tests can reach internal members). MVVM via CommunityToolkit.Mvvm (`[ObservableProperty]`,
`[RelayCommand]`); charts via LiveChartsCore.SkiaSharpView.WPF.

**Pipeline, in order** (driven by `ViewModels/MainViewModel.cs`, the single hub the views bind to):

1. **`Services/HistoryParser.cs`** — finds and parses the Spotify export JSON files (both the
   modern extended-history format and the older legacy `StreamingHistory*.json` format) into
   normalized `Models/PlayRecord.cs` objects.
2. **`Services/RecordCache.cs`** — a binary cache of parsed records, keyed by the exact set of
   source file paths + sizes + write times (`%LOCALAPPDATA%\Sortify\records.cache`). Reopening an
   unchanged export skips JSON parsing entirely; any change to the file set invalidates the key.
3. **`Services/FilterEngine.cs`** — applies the current `Models/FilterOptions` (date range, min
   play duration, search text, excluded artists/tracks, time-of-day/day-of-week windows, whether
   podcasts count) as a streaming filter over raw records.
4. **`Services/AnalysisEngine.cs`** — the aggregation core. Consumes filtered records and produces
   one `AnalysisResult` (`Models/Stats.cs`): per-track/artist/album/year stats, skip/completion
   rates, sessions, streaks, hour/day-of-week breakdowns, podcast stats, playback-context
   breakdowns (device/country/shuffle/offline). Runs on a background thread via `AnalyzeAsync`.
   Note it re-applies filtering itself with `ignoreMinDuration: true` so skip/reason-end stats can
   still see the short plays a duration cutoff would otherwise drop.
5. **`Services/ChartBuilder.cs`** — turns an `AnalysisResult` into `Services/ChartData.cs` /
   LiveCharts `ISeries[]` for each chart on screen. Charts bake colors into Skia paints at build
   time, so a theme change rebuilds charts rather than repainting them (see
   `ThemeService.Changed` in `MainViewModel`).
6. **`Services/DetailEngine.cs`** — builds the focused drill-down (`DetailResult`,
   `Models/DetailStats.cs`) shown in `Views/DetailWindow.xaml` when a track/artist/album/year row
   is double-clicked; reruns aggregation scoped to one entity under the current filters.
7. **`Services/ExportService.cs`** — writes the current `AnalysisResult` out as txt/Markdown/JSON/CSV.

Supporting services: `Services/AppSettings.cs` (JSON-persisted preferences — recent folders, theme,
session-gap minutes, chart animation toggle, the Time/Plays metric — failures degrade to defaults
rather than surfacing errors), `Services/ThemeService.cs` (swaps the palette `ResourceDictionary`
at a fixed slot in `App.xaml`'s merged dictionaries so every DynamicResource-bound style repaints;
raises `Changed`), `Services/ImageExporter.cs` (copy/save a chart as PNG, wired to chart
right-click), `Services/SearchSuggestions.cs` (the names a search, exclusion or table-filter box
suggests as you type, attached to a `TextBox` by `Views/SearchCompletion.cs`; behaviour described
in `docs/Search.md`), `Views/Converters.cs`.

**UI structure**: `Views/MainWindow.xaml(.cs)` is the main shell (tabs: Overview, Tracks, Artists,
Albums, Years, Podcasts, Trends, Insights, Compare Periods, Compare People) bound to `MainViewModel`.
Compare Periods (`ViewModels/CompareViewModel.cs`, `Services/PeriodComparison.cs`) puts two date
ranges of the loaded history side by side; Compare People (`ViewModels/PeopleCompareViewModel.cs`,
`Services/PeopleComparison.cs`) puts someone else's export next to yours, holding their plays in
memory only (never in the record cache or Recent). `Views/FilterPanel.xaml(.cs)`
+ `ViewModels/FilterViewModel.cs` is the filter sidebar, which raises `FiltersChanged` on any
change; `MainViewModel` debounces that (300ms `DispatcherTimer`) before re-running analysis.
Its dates are picked with `Views/DateSelector.xaml` (year, month and day drop-downs) and its time
of day with `Views/HourRangeSlider.xaml`.
`Views/DetailWindow.xaml(.cs)` is the per-row drill-down. `Views/SettingsWindow.xaml(.cs)` covers
theme/animation/session-gap/cache-clearing preferences. Every window derives from
`Views/ThemedWindow.cs`, which swaps the native title bar for a palette-coloured one via `WindowChrome`
(template and caption-button styles at the end of `Themes/Controls.xaml`); a new window should derive
from it too, or it gets the white Windows title bar. MainWindow's toolbar lives in that bar, via
`ThemedWindow.TitleBarContent`: its buttons take clicks, while the gaps and anything marked
`IsHitTestVisible="False"` (the logo, the name, the progress bar) still drag the window. Grid item
sources (`Tracks`, `Artists`, etc.) are swapped wholesale after each analysis pass rather than
mutated via `ObservableCollection`, since incremental updates to tens of thousands of rows would
raise a `CollectionChanged` per row and freeze the UI. Large horizontal bar charts
(tracks/artists/albums) page in via `LoadMore*` methods as the user scrolls rather than rendering
every bar up front.

**Time / Plays**: the segmented switch at the top of the filter sidebar (`Views/FilterPanel.xaml`,
bound to the window's view model rather than the panel's `FilterViewModel`) sets `MainViewModel.Metric` (`Models/Metric.cs`,
remembered in settings), which every chart and ranked list follows: bars, donuts, the over-time,
hour, weekday, heatmap and per-year charts, the Overview top fives, the Tracks/Artists/Albums/podcast
grids, each month's and year's leaders, the Compare Periods tab (`CompareViewModel.Metric`) and the
detail window. Compare People still ranks by listening time either way. `AnalysisResult` already
carries both measures (`Plays*` breakdowns, `*ByPlayCount` lists, `MostPlayed*` leaders), so
switching re-ranks and rebuilds charts without re-running analysis; new charts take a `Metric` in
`ChartBuilder`. Grid column headers draw a sort arrow (header template in
`Themes/Controls.xaml`); since the ranked grids arrive pre-sorted rather than sorted by the grid,
`Views/GridSort.cs`'s `PresortedBy` puts the arrow on the column they arrive sorted by. Top lists,
tables and ranked bar charts number their rows; a grid gets its "#" column from
`Views/RowNumbers.cs`.
Grid columns, which can't see the DataContext, bind headers and visibility through
`Views/BindingProxy.cs` (the `Vm` resource in MainWindow).

The logo's source is `Sortify/Assets/sortify-logo.svg`. `Themes/Logo.xaml` is the same drawing as a
WPF `DrawingImage` (key `SortifyLogo`) that the title bar, toolbar, Settings and the year-in-review
card draw, and `Assets/Sortify.ico` is rendered from it at 16-256 px for the exe, taskbar and Alt+Tab.
A change to the logo means updating all three.

Tests (`Sortify.Tests/`) that touch WPF types (windows, converters, dependency properties) run
through `WpfTestHost.cs`, which spins up a single STA thread with one live `Application` and merges
the theme/converter resources by hand (bypassing `App.xaml`'s `StartupUri` so no real window
opens). WPF only allows one `Application` per process, so this host is shared across all UI tests
rather than created per-test.
