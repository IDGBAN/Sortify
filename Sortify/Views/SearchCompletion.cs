using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Sortify.Services;

namespace Sortify.Views;

/// <summary>Data-backed completion that preserves the text box's existing filter binding.</summary>
public static class SearchCompletion
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(IReadOnlyList<SearchSuggestion>), typeof(SearchCompletion), new PropertyMetadata(null, SourceChanged));
    public static readonly DependencyProperty KindsProperty = DependencyProperty.RegisterAttached(
        "Kinds", typeof(string), typeof(SearchCompletion), new PropertyMetadata(string.Empty));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(Completion), typeof(SearchCompletion));

    public static void SetSource(DependencyObject target, IReadOnlyList<SearchSuggestion> value) => target.SetValue(SourceProperty, value);
    public static IReadOnlyList<SearchSuggestion>? GetSource(DependencyObject target) => (IReadOnlyList<SearchSuggestion>?)target.GetValue(SourceProperty);
    public static void SetKinds(DependencyObject target, string value) => target.SetValue(KindsProperty, value);
    public static string GetKinds(DependencyObject target) => (string)target.GetValue(KindsProperty);

    public static bool Dismiss(TextBox box)
    {
        if (box.GetValue(StateProperty) is not Completion state)
            return false;
        return state.Dismiss();
    }

    private static void SourceChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not TextBox box)
            return;
        if (box.GetValue(StateProperty) is not Completion state)
        {
            state = new Completion(box);
            box.SetValue(StateProperty, state);
        }
        state.Refresh();
    }

    private sealed class Completion
    {
        private readonly TextBox _box;
        private readonly Popup _popup;
        private readonly ListBox _list;
        private readonly TextBlock _hint;
        private bool _accepting;

        public Completion(TextBox box)
        {
            _box = box;
            _list = new ListBox { MaxHeight = 260, MinWidth = 280, Focusable = false };
            _list.SetResourceReference(ItemsControl.ItemTemplateProperty, "SearchSuggestionTemplate");
            _list.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "SearchSuggestionItem");
            _list.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
            _list.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            _hint = new TextBlock { Margin = new Thickness(8), Text = "No matches in loaded history." };
            _hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            var panel = new StackPanel();
            panel.Children.Add(_list);
            panel.Children.Add(_hint);
            var border = new Border { Child = panel, BorderThickness = new Thickness(1), Padding = new Thickness(4) };
            border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            _popup = new Popup { PlacementTarget = box, Placement = PlacementMode.Bottom, StaysOpen = false, Child = border };
            box.TextChanged += (_, _) => Refresh();
            box.GotKeyboardFocus += (_, _) => Refresh();
            box.LostKeyboardFocus += (_, _) => box.Dispatcher.BeginInvoke(() =>
            {
                if (!box.IsKeyboardFocusWithin && !_list.IsKeyboardFocusWithin)
                    _popup.IsOpen = false;
            });
            box.Unloaded += (_, _) => _popup.IsOpen = false;
            box.IsVisibleChanged += (_, _) => { if (!box.IsVisible) _popup.IsOpen = false; };
            box.PreviewKeyDown += OnKey;
            _list.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (ItemsControl.ContainerFromElement(_list, e.OriginalSource as DependencyObject) is ListBoxItem item
                    && item.Content is SearchSuggestion suggestion)
                {
                    Accept(suggestion);
                    e.Handled = true;
                }
            };
        }

        public bool Dismiss()
        {
            bool wasOpen = _popup.IsOpen;
            _popup.IsOpen = false;
            return wasOpen;
        }

        public void Refresh()
        {
            if (_accepting)
                return;
            var matches = SearchSuggestions.Find(GetSource(_box) ?? Array.Empty<SearchSuggestion>(), _box.Text, GetKinds(_box));
            _list.ItemsSource = matches;
            _hint.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _list.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _popup.IsOpen = _box.IsKeyboardFocused && _box.IsVisible && !string.IsNullOrWhiteSpace(_box.Text);
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (!_popup.IsOpen)
                return;
            if (e.Key == Key.Escape)
                _popup.IsOpen = false;
            else if (e.Key is Key.Down or Key.Up && _list.Items.Count > 0)
            {
                _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, _list.Items.Count - 1);
                _list.ScrollIntoView(_list.SelectedItem);
            }
            else if (e.Key == Key.Enter && _list.SelectedItem is SearchSuggestion selected)
                Accept(selected);
            else
                return;
            e.Handled = true;
        }

        private void Accept(SearchSuggestion suggestion)
        {
            _accepting = true;
            _box.SetCurrentValue(TextBox.TextProperty, suggestion.Name);
            _box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            _box.Focus();
            _box.CaretIndex = _box.Text.Length;
            _popup.IsOpen = false;
            _accepting = false;
        }
    }
}
