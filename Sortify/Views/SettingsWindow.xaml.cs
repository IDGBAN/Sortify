using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Sortify.Services;

namespace Sortify.Views;

/// <summary>
/// Preferences dialog. Changes apply immediately - there is no OK/Cancel pair, because
/// every setting here is reversible and seeing the theme change as you pick it is the
/// point.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    /// <summary>Suppresses change handlers while the controls are being seeded.</summary>
    private bool _loading = true;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        ThemeBox.SelectedIndex = _settings.Theme switch
        {
            AppTheme.Dark => 1,
            AppTheme.Light => 2,
            _ => 0,
        };
        AnimateBox.IsChecked = _settings.AnimateCharts;
        ReopenBox.IsChecked = _settings.ReopenLastFolder;
        GapSlider.Minimum = AppSettings.MinSessionGapMinutes;
        GapSlider.Maximum = AppSettings.MaxSessionGapMinutes;
        GapSlider.Value = _settings.SessionGapMinutes;
        GapValue.Text = _settings.SessionGapMinutes.ToString();

        FolderText.Text = RecordCache.CacheDirectory;
        VersionText.Text = $"Sortify {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?"}";
        RefreshCacheText();
        RefreshRecentText();

        _loading = false;
    }

    /// <summary>True when something changed that the main window needs to react to.</summary>
    public bool NeedsRefresh { get; private set; }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not ComboBoxItem { Tag: string tag })
            return;

        if (!Enum.TryParse<AppTheme>(tag, out var theme))
            return;

        _settings.Theme = theme;
        _settings.Save();
        ThemeService.Apply(theme);
    }

    private void OnAnimateChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.AnimateCharts = AnimateBox.IsChecked == true;
        _settings.Save();
        NeedsRefresh = true;
    }

    private void OnReopenChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.ReopenLastFolder = ReopenBox.IsChecked == true;
        _settings.Save();
    }

    private void OnGapChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int minutes = (int)Math.Round(e.NewValue);
        GapValue.Text = minutes.ToString();
        if (_loading) return;

        // Not saved here: dragging the slider fires this once per minute crossed, and each
        // save is a synchronous file write. OnClosed persists the final value.
        _settings.SessionGapMinutes = minutes;
        // Session counts are computed during analysis, so the results have to be redone.
        NeedsRefresh = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _settings.Save();
        base.OnClosed(e);
    }

    private void OnClearCache(object sender, RoutedEventArgs e)
    {
        RecordCache.Clear();
        RefreshCacheText();
    }

    private void OnClearRecent(object sender, RoutedEventArgs e)
    {
        _settings.RecentFolders.Clear();
        _settings.LastFolder = null;
        _settings.Save();
        RefreshRecentText();
        NeedsRefresh = true;
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(RecordCache.CacheDirectory);
            Process.Start(new ProcessStartInfo(RecordCache.CacheDirectory) { UseShellExecute = true });
        }
        // The shell throws Win32Exception when no handler is registered, which is not an
        // IOException and would otherwise reach the app-level crash dialog.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                     or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "Could not open that folder.", "Sortify",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void RefreshCacheText()
    {
        long bytes = RecordCache.SizeBytes();
        CacheText.Text = bytes == 0
            ? "No parsed history is cached right now."
            : $"Parsed history cache: {bytes / 1024.0 / 1024.0:0.0} MB. " +
              "Clearing it just means the next load re-reads your JSON files.";
        ClearCacheButton.IsEnabled = bytes > 0;
    }

    private void RefreshRecentText()
    {
        int count = _settings.RecentFolders.Count;
        RecentText.Text = count == 0
            ? "No folders are remembered."
            : $"{count} folder{(count == 1 ? "" : "s")} in the Recent menu.";
        ClearRecentButton.IsEnabled = count > 0;
    }
}
