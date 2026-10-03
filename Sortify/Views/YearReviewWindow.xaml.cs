using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using Sortify.Models;
using Sortify.Services;

namespace Sortify.Views;

/// <summary>Previews a year-in-review card and saves or copies it as an image.</summary>
public partial class YearReviewWindow : Window
{
    private readonly YearReview _review;
    private readonly YearReviewCard? _card;

    public YearReviewWindow(YearReview review)
    {
        InitializeComponent();
        _review = review;
        Title = $"{review.Year} in Review";

        if (review.Plays == 0)
        {
            Preview.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Visible;
            EmptyText.Text = $"There are no plays in {review.Year} under the current filters, so there's nothing to put on a card.";
            SaveButton.IsEnabled = false;
            CopyButton.IsEnabled = false;
            return;
        }

        _card = new YearReviewCard(review);
        CardHost.Child = _card;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_card is null)
            return;

        var dialog = new SaveFileDialog
        {
            Filter = "PNG images (*.png)|*.png",
            DefaultExt = ".png",
            FileName = $"Sortify_{_review.Year}_in_review.png",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            // The card is laid out at its final pixel size, so it is rendered as is.
            bool saved = ImageExporter.SavePng(_card, null, dialog.FileName, scale: 1);
            ResultText.Text = saved ? $"Saved to {dialog.FileName}" : "There was nothing to save.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ResultText.Text = $"Could not save the image: {ex.Message}";
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_card is null)
            return;

        try
        {
            ResultText.Text = ImageExporter.CopyToClipboard(_card, null, scale: 1)
                ? "Copied to the clipboard."
                : "There was nothing to copy.";
        }
        catch (ExternalException ex)
        {
            // The clipboard can be locked by another process.
            ResultText.Text = $"Could not copy the image: {ex.Message}";
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
