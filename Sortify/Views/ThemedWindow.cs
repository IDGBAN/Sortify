using System.Collections;
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
    public static readonly DependencyProperty TitleBarHeightProperty = DependencyProperty.Register(
        nameof(TitleBarHeight), typeof(double), typeof(ThemedWindow),
        new PropertyMetadata(32.0, (d, _) => ((ThemedWindow)d).UpdateChrome()));

    public static readonly DependencyProperty TitleBarBackgroundProperty = DependencyProperty.Register(
        nameof(TitleBarBackground), typeof(Brush), typeof(ThemedWindow));

    public static readonly DependencyProperty TitleBarContentProperty = DependencyProperty.Register(
        nameof(TitleBarContent), typeof(object), typeof(ThemedWindow),
        new PropertyMetadata(null, OnTitleBarContentChanged));

    public static readonly DependencyProperty ShowTitleProperty = DependencyProperty.Register(
        nameof(ShowTitle), typeof(bool), typeof(ThemedWindow), new PropertyMetadata(true));

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

    /// <summary>Height of the drawn title bar, which is also how far down the window drags.</summary>
    public double TitleBarHeight
    {
        get => (double)GetValue(TitleBarHeightProperty);
        set => SetValue(TitleBarHeightProperty, value);
    }

    /// <summary>Fill behind the title bar. The style defaults it to the window background.</summary>
    public Brush? TitleBarBackground
    {
        get => (Brush?)GetValue(TitleBarBackgroundProperty);
        set => SetValue(TitleBarBackgroundProperty, value);
    }

    /// <summary>
    /// Controls drawn in the title bar, between the title and the caption buttons - MainWindow's
    /// toolbar. They are clickable; gaps between them and anything set IsHitTestVisible="False"
    /// still drag the window.
    /// </summary>
    public object? TitleBarContent
    {
        get => GetValue(TitleBarContentProperty);
        set => SetValue(TitleBarContentProperty, value);
    }

    /// <summary>
    /// Whether the title bar shows the logo and title. MainWindow turns it off because its
    /// toolbar, in the bar, carries both; the taskbar and Alt+Tab still show the title.
    /// </summary>
    public bool ShowTitle
    {
        get => (bool)GetValue(ShowTitleProperty);
        set => SetValue(ShowTitleProperty, value);
    }

    // Made a logical child, as Window.Content is, so it inherits the window's DataContext and
    // resources directly rather than through the template.
    protected override IEnumerator LogicalChildren
    {
        get
        {
            var children = new ArrayList();
            for (var e = base.LogicalChildren; e?.MoveNext() == true;)
                children.Add(e.Current);
            if (TitleBarContent is { } content)
                children.Add(content);
            return children.GetEnumerator();
        }
    }

    private static void OnTitleBarContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var window = (ThemedWindow)d;
        window.RemoveLogicalChild(e.OldValue);
        window.AddLogicalChild(e.NewValue);

        // Without this the caption swallows every click in the bar. Set on the content itself:
        // it inherits down the content's own tree, whereas a value on the template's presenter
        // would not reach a logical child of the window.
        if (e.NewValue is UIElement content)
            WindowChrome.SetIsHitTestVisibleInChrome(content, true);
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
