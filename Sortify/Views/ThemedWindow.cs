using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace Sortify.Views;

/// <summary>
/// Base for every Sortify window. Replaces the title bar Windows draws - white whatever the
/// theme - with one painted from the palette. WindowChrome keeps the native behaviour (drag,
/// Aero Snap, double-click to maximize, the right-click and Alt+Space menu, edge resizing)
/// while the template in Themes/Controls.xaml supplies the look.
/// </summary>
public class ThemedWindow : Window
{
    /// <summary>Height of the drawn title bar. The template sizes the bar from this.</summary>
    public const double TitleBarHeight = 32;

    public static readonly DependencyProperty TitleBarBackgroundProperty = DependencyProperty.Register(
        nameof(TitleBarBackground), typeof(Brush), typeof(ThemedWindow));

    // Windows draws an invisible resize band outside a normal window; with the frame removed
    // the band has to come out of the window itself.
    private const double ResizeBorder = 6;

    private const int SmCxSizeFrame = 32;
    private const int SmCxPaddedBorder = 92;

    private readonly WindowChrome _chrome = new()
    {
        UseAeroCaptionButtons = false,
        // A sliver of glass is what keeps the DWM drop shadow; with none the window loses it.
        // The template paints over it, so it never shows.
        GlassFrameThickness = new Thickness(0, 0, 0, 1),
    };

    private FrameworkElement? _frame;

    public ThemedWindow()
    {
        // WPF finds implicit styles by exact type, so one keyed on this class would never reach
        // MainWindow and the rest. Point every subclass at it; a window's own Style still wins.
        SetResourceReference(StyleProperty, typeof(ThemedWindow));
        WindowChrome.SetWindowChrome(this, _chrome);

        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (_, _) => SystemCommands.MaximizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (_, _) => SystemCommands.RestoreWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => SystemCommands.CloseWindow(this)));
    }

    /// <summary>
    /// Fill behind the title and caption buttons. The style defaults it to the window background;
    /// MainWindow sets the toolbar's colour so the two read as one bar.
    /// </summary>
    public Brush? TitleBarBackground
    {
        get => (Brush?)GetValue(TitleBarBackgroundProperty);
        set => SetValue(TitleBarBackgroundProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _frame = GetTemplateChild("PART_Frame") as FrameworkElement;
        UpdateChrome();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateChrome();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateChrome();
    }

    /// <summary>
    /// Keeps the draggable caption lined up with the drawn title bar. WindowChrome measures the
    /// caption from the window's top edge, resize band first, and Windows places a maximized
    /// window with that edge hanging off the screen by the width of the frame it expects to hide.
    /// </summary>
    private void UpdateChrome()
    {
        bool maximized = WindowState == WindowState.Maximized;
        bool resizable = ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;

        double inset = maximized ? MaximizedFrameWidth() : 0;
        double resize = resizable && !maximized ? ResizeBorder : 0;

        if (_frame is not null)
            _frame.Margin = new Thickness(inset);

        _chrome.ResizeBorderThickness = new Thickness(resize);
        _chrome.CaptionHeight = inset + TitleBarHeight - resize;
    }

    /// <summary>How far past each screen edge Windows puts a maximized window, in DIPs.</summary>
    private double MaximizedFrameWidth()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        uint dpiValue = (uint)Math.Round(dpi.PixelsPerInchX);
        int frame = GetSystemMetricsForDpi(SmCxSizeFrame, dpiValue) + GetSystemMetricsForDpi(SmCxPaddedBorder, dpiValue);
        return frame / dpi.DpiScaleX;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
