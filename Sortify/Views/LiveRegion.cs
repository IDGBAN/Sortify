using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace Sortify.Views;

/// <summary>
/// A text block that screen readers read out when its message changes or appears, set as
/// <c>views:LiveRegion.Announce="Polite"</c> (or "Assertive"). AutomationProperties.LiveSetting
/// alone isn't enough: WPF only announces a live region when its automation peer raises
/// LiveRegionChanged, and nothing raises it by itself.
/// </summary>
public static class LiveRegion
{
    public static readonly DependencyProperty AnnounceProperty = DependencyProperty.RegisterAttached(
        "Announce", typeof(AutomationLiveSetting), typeof(LiveRegion),
        new PropertyMetadata(AutomationLiveSetting.Off, OnAnnounceChanged));

    public static AutomationLiveSetting GetAnnounce(DependencyObject element) =>
        (AutomationLiveSetting)element.GetValue(AnnounceProperty);

    public static void SetAnnounce(DependencyObject element, AutomationLiveSetting value) =>
        element.SetValue(AnnounceProperty, value);

    // Held by the text block itself, so the watcher lives exactly as long as what it watches.
    private static readonly DependencyProperty WatcherProperty = DependencyProperty.RegisterAttached(
        "Watcher", typeof(Watcher), typeof(LiveRegion));

    /// <summary>Raised for each announcement, screen reader or not, so tests can see them.</summary>
    internal static event Action<UIElement>? Announcing;

    private static void OnAnnounceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block)
            return;

        var setting = (AutomationLiveSetting)e.NewValue;
        AutomationProperties.SetLiveSetting(block, setting);
        if (setting != AutomationLiveSetting.Off && block.GetValue(WatcherProperty) is null)
        {
            block.SetValue(WatcherProperty, new Watcher(block));
        }
        else if (setting == AutomationLiveSetting.Off && block.GetValue(WatcherProperty) is Watcher watcher)
        {
            watcher.Stop();
            block.ClearValue(WatcherProperty);
        }
    }

    /// <summary>
    /// Reads out an element whose AutomationProperties.LiveSetting is set, now: for a message
    /// that isn't a text block's text, such as a button saying whether it worked.
    /// </summary>
    internal static void Announce(UIElement element)
    {
        Announcing?.Invoke(element);
        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            UIElementAutomationPeer.CreatePeerForElement(element).RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    /// <summary>
    /// Follows a block's text and its own visibility through bindings. A message counts as new
    /// when either changes after the block is on screen; a window opening, or a tab coming into
    /// view, says nothing, since the screen reader reads those itself.
    /// </summary>
    private sealed class Watcher : DependencyObject
    {
        private static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            "Text", typeof(string), typeof(Watcher), new PropertyMetadata(null, OnWatchedChanged));

        private static readonly DependencyProperty VisibilityProperty = DependencyProperty.Register(
            "Visibility", typeof(Visibility), typeof(Watcher), new PropertyMetadata(Visibility.Visible, OnWatchedChanged));

        private readonly TextBlock _block;
        private bool _watching;

        // A message that appears and changes at once, as most do, is one announcement.
        private bool _pending;

        public Watcher(TextBlock block)
        {
            _block = block;
            BindingOperations.SetBinding(this, TextProperty, new Binding(nameof(TextBlock.Text)) { Source = block });
            BindingOperations.SetBinding(this, VisibilityProperty, new Binding(nameof(UIElement.Visibility)) { Source = block });
            _watching = true;
        }

        public void Stop()
        {
            _watching = false;
            BindingOperations.ClearAllBindings(this);
        }

        private static void OnWatchedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((Watcher)d).Changed();

        private void Changed()
        {
            if (!_watching || _pending || !_block.IsLoaded)
                return;

            // Once layout has caught up, so a block that just appeared is in the automation tree.
            _pending = true;
            _block.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                _pending = false;
                if (_watching && _block.IsVisible && !string.IsNullOrWhiteSpace(_block.Text))
                    Announce(_block);
            });
        }
    }
}
