using System.Windows;
using Sortify.Models;
using Sortify.Services;

namespace Sortify.Views;

/// <summary>Lists the files the history on screen was read from, what each held, and what was left out.</summary>
public partial class LoadedFilesWindow : ThemedWindow
{
    /// <summary>One file as the list shows it.</summary>
    public sealed record FileRow(string Name, string Path, string Format, string Details, bool IsProblem);

    public LoadedFilesWindow(LoadedHistory history)
    {
        InitializeComponent();

        SummaryText.Text = Summary(history);
        SetLine(SourceText, SourceLine(history));
        SetLine(DuplicatesText, history.DuplicatesRemoved > 0
            ? $"Left out {Plural.Of(history.DuplicatesRemoved, "play")} " +
              "that more than one of the files held, so each counts once."
            : string.Empty);
        AccountDataNote.Visibility = OnlyAccountData(history) ? Visibility.Visible : Visibility.Collapsed;
        FilesCard.Visibility = history.IsSample ? Visibility.Collapsed : Visibility.Visible;
        FilesList.ItemsSource = history.Files.Select(Row).ToList();
    }

    private static void SetLine(System.Windows.Controls.TextBlock block, string text)
    {
        block.Text = text;
        block.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>"Read 377,466 plays from 12 files, covering 2014-03-02 to 2024-06-01."</summary>
    internal static string Summary(LoadedHistory history)
    {
        string plays = Plural.Of(history.Plays, "play");
        bool dated = history.First is not null && history.Last is not null;
        string from = dated ? TimeFormat.Day(history.First!.Value) : string.Empty;
        string to = dated ? TimeFormat.Day(history.Last!.Value) : string.Empty;
        if (history.IsSample)
            return dated ? $"A made-up sample history: {plays} from {from} to {to}." : $"A made-up sample history: {plays}.";

        int read = history.Files.Count(f => f.Format != LoadedFileFormat.Skipped);
        string files = Plural.Of(read, "file");
        return dated ? $"Read {plays} from {files}, covering {from} to {to}." : $"Read {plays} from {files}.";
    }

    internal static string SourceLine(LoadedHistory history)
    {
        if (history.IsSample)
            return "Nothing was read from disk. Open your own export to see yours.";

        var parts = new List<string>();
        if (history.Source is { } source)
            parts.Add($"From {source}.");
        if (history.FromCache)
            parts.Add("Read from Sortify's cache, since none of the files has changed since it last read them.");
        parts.Add("The other tabs count the plays the sidebar's filters let through.");
        return string.Join(" ", parts);
    }

    /// <summary>True when what was read is all the older account-data export, which lacks most of what the app shows.</summary>
    internal static bool OnlyAccountData(LoadedHistory history) =>
        history.Files.Any(f => f.Format == LoadedFileFormat.AccountData) &&
        history.Files.All(f => f.Format != LoadedFileFormat.Extended);

    internal static FileRow Row(LoadedFile file)
    {
        if (file.Format == LoadedFileFormat.Skipped)
            return new FileRow(file.Name, file.Path, "Skipped", file.Problem ?? "Not read", true);

        string plays = Plural.Of(file.Plays, "play");
        string details = file.First is { } first && file.Last is { } last
            ? $"{plays}, {TimeFormat.Day(first)} to {TimeFormat.Day(last)}"
            : plays;
        return new FileRow(file.Name, file.Path,
            file.Format == LoadedFileFormat.Extended ? "Extended history" : "Account data", details, false);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
