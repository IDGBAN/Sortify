using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>The two-thumb hour range in the filter sidebar.</summary>
[Collection(WpfCollection.Name)]
public class HourRangeSliderTests
{
    [Fact]
    public void BothThumbs_TakeTheKeyboard_AndSayTheirHour() => WpfTestHost.Run(() =>
    {
        var slider = new HourRangeSlider { Width = 240 };
        var window = new Window { Content = slider, Width = 300, Height = 120, ShowInTaskbar = false };
        try
        {
            window.Show();
            var start = (Thumb)slider.FindName("StartThumb");
            var end = (Thumb)slider.FindName("EndThumb");
            Assert.True(start.Focusable && start.IsTabStop);
            Assert.True(end.Focusable && end.IsTabStop);

            Assert.True(start.Focus());
            Press(start, Key.Right);
            Press(start, Key.Right);
            Press(end, Key.Left);

            Assert.Equal(2, slider.Start);
            Assert.Equal(22, slider.End);
            Assert.Equal("Start hour, 02:00", AutomationProperties.GetName(start));
            Assert.Equal("End hour, 22:59", AutomationProperties.GetName(end));

            Press(end, Key.Home);
            Assert.Equal(0, slider.End);
        }
        finally
        {
            window.Close();
        }

        static void Press(UIElement target, Key key) => target.RaiseEvent(
            new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            });
    });
}
