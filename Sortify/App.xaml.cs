using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Sortify.Services;

namespace Sortify;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>Preferences loaded once at startup and shared with the main view model.</summary>
    public AppSettings Settings { get; } = AppSettings.Load();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Applying the theme before the first window is created avoids a visible flash of
        // the wrong palette, and configures LiveCharts' own light/dark theme to match.
        ThemeService.Apply(Settings.Theme);
        ThemeService.WatchSystemTheme();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeService.StopWatchingSystemTheme();
        base.OnExit(e);
    }

    /// <summary>
    /// Last line of defence. Anything that reaches here would otherwise take the whole
    /// window down with a Windows crash dialog and no explanation; showing the message and
    /// carrying on lets the user save or export what they were looking at.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        TryWriteCrashLog(e.Exception);

        MessageBox.Show(
            MainWindow,
            $"Something went wrong:\n\n{e.Exception.Message}\n\n" +
            "The app is still running - if it is behaving oddly, restart it. " +
            $"Details were written to the log in {AppPaths.DataDirectory}.",
            "Sortify", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static void TryWriteCrashLog(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.AppendAllText(AppPaths.ErrorLog, $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never be the thing that brings the app down.
        }
    }
}
