using System.Windows;
using System.Windows.Threading;
using Xunit.Sdk;

namespace Sortify.Tests;

/// <summary>
/// A single STA thread with a running dispatcher and one <see cref="Application"/>, shared by
/// every UI test. WPF allows only one Application per process, so this cannot be per-test.
/// </summary>
internal static class WpfTestHost
{
    private static readonly object Gate = new();
    private static Dispatcher? _dispatcher;

    /// <summary>Runs <paramref name="action"/> on the UI thread and rethrows anything it threw.</summary>
    public static void Run(Action action)
    {
        var dispatcher = EnsureStarted();

        Exception? failure = null;
        dispatcher.Invoke(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });

        if (failure is not null)
            throw new XunitException($"UI test failed on the WPF thread: {failure}");
    }

    private static Dispatcher EnsureStarted()
    {
        lock (Gate)
        {
            if (_dispatcher is not null)
                return _dispatcher;

            var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                // OnExplicitShutdown keeps the app alive even though no window is ever shown.
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                LoadApplicationResources(app);

                _dispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "WPF test host",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait(TimeSpan.FromSeconds(30));

            return _dispatcher ?? throw new XunitException("The WPF test host did not start.");
        }
    }

    /// <summary>
    /// Mirrors what App.xaml merges. The generated App class runs StartupUri and would open
    /// the real window, so the dictionaries are merged by hand instead.
    /// </summary>
    private static void LoadApplicationResources(Application app)
    {
        foreach (var source in new[] { "Themes/Dark.xaml", "Themes/Controls.xaml" })
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Sortify;component/{source}"),
            });
        }

        app.Resources["BoolToVis"] = new System.Windows.Controls.BooleanToVisibilityConverter();
        app.Resources["NotBoolToVis"] = new Sortify.Views.Converters.InverseBoolToVisibilityConverter();
        app.Resources["TextToVis"] = new Sortify.Views.Converters.EmptyStringToVisibilityConverter();
        app.Resources["EmptyToVis"] = new Sortify.Views.Converters.EmptyCollectionToVisibilityConverter();
        app.Resources["HhMmSs"] = new Sortify.Views.Converters.DurationConverter();
    }
}
