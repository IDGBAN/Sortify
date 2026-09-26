using System.IO;
using System.Windows;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Win32;

namespace Sortify.Services;

/// <summary>Which palette the user asked for.</summary>
public enum AppTheme
{
    /// <summary>Follow the Windows "app mode" setting.</summary>
    System,
    Dark,
    Light,
}

/// <summary>
/// Swaps the active palette dictionary at runtime. Every style reaches its colours through
/// DynamicResource, so replacing the merged dictionary at <see cref="PaletteSlot"/> repaints
/// the whole window without recreating any windows.
/// </summary>
public static class ThemeService
{
    /// <summary>Index of the palette inside App.xaml's merged dictionaries.</summary>
    private const int PaletteSlot = 0;

    // Absolute pack URIs rather than relative ones: a relative URI resolves against whatever
    // assembly happens to be running the show, which is not Sortify under a test host.
    private static readonly Uri DarkUri = new("pack://application:,,,/Sortify;component/Themes/Dark.xaml");
    private static readonly Uri LightUri = new("pack://application:,,,/Sortify;component/Themes/Light.xaml");

    /// <summary>Raised after the palette changes, so charts can be rebuilt in the new colours.</summary>
    public static event EventHandler? Changed;

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    /// <summary>True when the palette currently in effect is the dark one.</summary>
    public static bool IsDark { get; private set; } = true;

    /// <summary>
    /// False until the first <see cref="Apply"/>. App.xaml merges the dark palette itself,
    /// so the state above already matches at startup - but LiveCharts has not been told
    /// which theme to paint its legends and tooltips with, and skipping the first call as
    /// redundant would leave them on the library's light default.
    /// </summary>
    private static bool _applied;

    /// <summary>
    /// Applies a theme. Safe to call before any window exists, and a no-op when the
    /// resolved palette is already the one in effect.
    /// </summary>
    public static void Apply(AppTheme theme)
    {
        bool dark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => SystemPrefersDark(),
        };

        bool sameTheme = Current == theme;
        Current = theme;

        if (_applied && sameTheme && dark == IsDark)
            return;

        _applied = true;
        IsDark = dark;
        SwapDictionary(dark);
        ChartPalette.Apply(dark);

        // LiveCharts paints legends, tooltips and default series from its own theme.
        LiveCharts.Configure(config =>
        {
            if (dark) config.AddDarkTheme();
            else config.AddLightTheme();
        });

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>The theme that follows this one when the toggle button is pressed.</summary>
    public static AppTheme Next() => IsDark ? AppTheme.Light : AppTheme.Dark;

    /// <summary>
    /// Keeps <see cref="AppTheme.System"/> in step with Windows while the app runs, rather than
    /// only reading the setting at launch. Pair with <see cref="StopWatchingSystemTheme"/>:
    /// SystemEvents holds its handlers statically and leaks anything left subscribed.
    /// </summary>
    public static void WatchSystemTheme() => SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

    public static void StopWatchingSystemTheme() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Switching light/dark mode in Windows arrives as General; this can be raised off the
        // UI thread, and Apply touches application resources.
        if (e.Category != UserPreferenceCategory.General)
            return;

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (Current == AppTheme.System)
                Apply(AppTheme.System);
        });
    }

    private static void SwapDictionary(bool dark)
    {
        var app = Application.Current;
        if (app is null)
            return;

        var dictionary = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > PaletteSlot)
            merged[PaletteSlot] = dictionary;
        else
            merged.Insert(0, dictionary);
    }

    /// <summary>
    /// Reads the Windows "choose your mode" setting. Defaults to dark when the value is
    /// missing or unreadable, which matches how the app has always looked.
    /// </summary>
    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return true;
        }
    }
}
