using System.Windows;
using System.Windows.Controls;

namespace Sortify.Views;

/// <summary>
/// Content drawn at the right-hand end of a TabControl's row of tabs, by the TabControl
/// template in Themes/Controls.xaml. MainWindow puts its Time / Plays switch there, beside
/// the tabs whose charts and lists it changes.
/// </summary>
public static class TabStrip
{
    public static readonly DependencyProperty TrailingContentProperty = DependencyProperty.RegisterAttached(
        "TrailingContent", typeof(object), typeof(TabStrip), new PropertyMetadata(null));

    public static object? GetTrailingContent(TabControl tabs) => tabs.GetValue(TrailingContentProperty);
    public static void SetTrailingContent(TabControl tabs, object? value) => tabs.SetValue(TrailingContentProperty, value);
}
