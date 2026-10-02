using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Sortify.Views;

public partial class FilterPanel : UserControl
{
    public FilterPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Keeps the minimum-duration box to digits. The binding target is an int, so anything
    /// else silently fails to reach the view model and leaves the user typing into a box
    /// that quietly ignores them.
    /// </summary>
    private void OnDigitsOnlyInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !IsAllDigits(e.Text);

    private void OnDigitsOnlyPaste(object sender, DataObjectPastingEventArgs e)
    {
        var pasted = e.DataObject.GetDataPresent(DataFormats.UnicodeText)
            ? e.DataObject.GetData(DataFormats.UnicodeText) as string
            : null;

        if (pasted is null || !IsAllDigits(pasted))
            e.CancelCommand();
    }

    private static bool IsAllDigits(string text)
    {
        if (text.Length == 0)
            return false;

        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }
        return true;
    }
}
