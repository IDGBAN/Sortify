using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Sortify.Views;

/// <summary>
/// One track with two thumbs for an hour-of-day range (0-23). The thumbs may cross: Start
/// greater than End means the range wraps past midnight, which is drawn as two filled
/// segments at either end of the track.
/// </summary>
public partial class HourRangeSlider : UserControl
{
    private const int MaxHour = 23;
    private const double ThumbSize = 16;

    public static readonly DependencyProperty StartProperty = DependencyProperty.Register(
        nameof(Start), typeof(int), typeof(HourRangeSlider),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceHour));

    public static readonly DependencyProperty EndProperty = DependencyProperty.Register(
        nameof(End), typeof(int), typeof(HourRangeSlider),
        new FrameworkPropertyMetadata(MaxHour, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceHour));

    public HourRangeSlider()
    {
        InitializeComponent();
    }

    public int Start
    {
        get => (int)GetValue(StartProperty);
        set => SetValue(StartProperty, value);
    }

    public int End
    {
        get => (int)GetValue(EndProperty);
        set => SetValue(EndProperty, value);
    }

    private static object CoerceHour(DependencyObject d, object value) => Math.Clamp((int)value, 0, MaxHour);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((HourRangeSlider)d).UpdateLayoutOfParts();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutOfParts();

    private double Span => Math.Max(0, Root.ActualWidth - ThumbSize);

    private double XOf(int hour) => hour / (double)MaxHour * Span;

    private int HourAt(double x) => Span <= 0 ? 0 : (int)Math.Round(Math.Clamp(x, 0, Span) / Span * MaxHour);

    private void UpdateLayoutOfParts()
    {
        if (!IsLoaded && Root.ActualWidth <= 0)
            return;

        double start = XOf(Start), end = XOf(End);
        Canvas.SetLeft(StartThumb, start);
        Canvas.SetLeft(EndThumb, end);

        double half = ThumbSize / 2;
        double width = Root.ActualWidth;
        if (Start <= End)
        {
            Place(FillA, start + half, end + half);
            Place(FillB, 0, 0);
        }
        else
        {
            Place(FillA, 0, end + half);
            Place(FillB, start + half, width);
        }
    }

    private static void Place(Border fill, double from, double to)
    {
        Canvas.SetLeft(fill, from);
        fill.Width = Math.Max(0, to - from);
    }

    private void OnStartDrag(object sender, DragDeltaEventArgs e)
        => Start = HourAt(Canvas.GetLeft(StartThumb) + e.HorizontalChange);

    private void OnEndDrag(object sender, DragDeltaEventArgs e)
        => End = HourAt(Canvas.GetLeft(EndThumb) + e.HorizontalChange);

    private void OnStartKey(object sender, KeyEventArgs e) => e.Handled = Step(e.Key, Start, v => Start = v);

    private void OnEndKey(object sender, KeyEventArgs e) => e.Handled = Step(e.Key, End, v => End = v);

    private static bool Step(Key key, int current, Action<int> set)
    {
        switch (key)
        {
            case Key.Left or Key.Down: set(current - 1); return true;
            case Key.Right or Key.Up: set(current + 1); return true;
            case Key.Home: set(0); return true;
            case Key.End: set(MaxHour); return true;
            default: return false;
        }
    }
}
