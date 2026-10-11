using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

[Collection(WpfCollection.Name)]
public class SearchSuggestionTests
{
    [Fact]
    public void SuggestionsAggregateHistoryAndKeepSameNamedTracksDistinct()
    {
        var index = SearchSuggestions.Build(new[]
        {
            Play("Echo", "Alice", 60000), Play("Echo", "Alice", 120000), Play("Echo", "Bob", 30000),
        });
        var artist = Assert.Single(SearchSuggestions.Find(index, " alice ", "Artist"));
        Assert.Equal(180000, artist.Milliseconds);
        Assert.Equal(2, artist.Plays);
        Assert.Contains("3m", artist.Summary);
        Assert.Equal(2, SearchSuggestions.Find(index, "echo", "Track").Count);
        Assert.Empty(SearchSuggestions.Find(index, "missing", ""));
        Assert.Empty(SearchSuggestions.Find(index, " ", ""));
        Assert.Empty(SearchSuggestions.Find(Array.Empty<SearchSuggestion>(), "alice", ""));
    }

    [Fact]
    public void ExactMatchesBeatPopularPartialMatchesAndResultsAreBounded()
    {
        var index = Enumerable.Range(0, 20).Select(i => new SearchSuggestion("Artist", $"Alice {i}", "", 100000, 1))
            .Append(new SearchSuggestion("Artist", "Alice", "", 1, 1)).ToArray();
        var found = SearchSuggestions.Find(index, "ALICE", "Artist");
        Assert.Equal("Alice", found[0].Name);
        Assert.Equal(8, found.Count);
    }

    [Fact]
    public void PodcastSuggestionsIncludeShowAndEpisodeDetails()
    {
        var index = SearchSuggestions.Build(new[] { new PlayRecord
        {
            Kind = ContentKind.Podcast, ShowName = "Science", EpisodeName = "Stars", MsPlayed = 60000,
        } });
        Assert.Equal("Science", Assert.Single(SearchSuggestions.Find(index, "stars", "Episode")).Secondary);
        Assert.Equal(60000, Assert.Single(SearchSuggestions.Find(index, "science", "Show")).Milliseconds);
        Assert.DoesNotContain(index, x => x.Kind is "Artist" or "Track" or "Album");
        Assert.Single(SearchSuggestions.Find(index, "science", "Episode"));
    }

    [Fact]
    public void TableSuggestionsMatchArtistsAndRespectTheCurrentFilters()
    {
        var records = new[] { Play("Echo", "Alice", 60000), Play("Other", "Bob", 120000) };
        var result = AnalysisEngine.Analyze(records, new FilterOptions { SearchTerm = "Alice" });
        var index = SearchSuggestions.FromResult(result);
        var track = Assert.Single(SearchSuggestions.Find(index, "alice", "Track"));
        Assert.Equal("Echo", track.Name);
        Assert.Equal(60000, track.Milliseconds);
        Assert.StartsWith("Current filters:", track.Summary);
        Assert.Single(SearchSuggestions.Find(index, "alice", "Album"));
        Assert.Empty(SearchSuggestions.Find(index, "bob", "Artist"));
        Assert.Empty(SearchSuggestions.FromResult(new AnalysisResult()));
    }

    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    public void CompletionSupportsKeyboardSelectionEmptyResultsAndReplacingHistory(AppTheme theme) => WpfTestHost.Run(() =>
    {
        ThemeService.Apply(theme);
        var filters = new FilterViewModel();
        var box = new TextBox { DataContext = filters };
        box.SetBinding(TextBox.TextProperty, new Binding(nameof(FilterViewModel.SearchTerm))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            Delay = 400,
        });
        var window = new Window { Content = box, Width = 400, Height = 160, ShowInTaskbar = false };
        try
        {
            SearchCompletion.SetKinds(box, "Artist");
            SearchCompletion.SetSource(box, SearchSuggestions.Build(new[] { Play("Echo", "Alice", 60000) }));
            window.Show();
            window.Activate();
            box.Focus();
            Keyboard.Focus(box);
            box.Text = "ali";
            var stateProperty = (DependencyProperty)typeof(SearchCompletion).GetField("StateProperty", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            object state = box.GetValue(stateProperty);
            var popup = (Popup)state.GetType().GetField("_popup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
            var list = (ListBox)state.GetType().GetField("_list", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
            Assert.True(popup.IsOpen);
            Assert.Single(list.Items);
            Press(Key.Down);
            Press(Key.Enter);
            Assert.Equal("Alice", box.Text);
            Assert.Equal("Alice", filters.SearchTerm);
            Assert.NotNull(box.GetBindingExpression(TextBox.TextProperty));
            Assert.False(popup.IsOpen);
            box.Text = "al";
            list.UpdateLayout();
            var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.NotNull(item);
            var labels = Descendants(item).OfType<TextBlock>().Select(x => x.Text).ToList();
            Assert.Contains("Alice", labels);
            Assert.Contains("Artist", labels);
            Assert.Contains(((SearchSuggestion)item.Content).Summary, labels);
            item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Mouse.PreviewMouseUpEvent,
            });
            Assert.Equal("Alice", filters.SearchTerm);
            Assert.False(popup.IsOpen);
            box.Text = "unknown";
            Assert.Empty(list.Items);
            Assert.True(popup.IsOpen);
            Press(Key.Escape);
            Assert.False(popup.IsOpen);
            box.Text = "ali";
            SearchCompletion.SetSource(box, Array.Empty<SearchSuggestion>());
            Assert.Empty(list.Items);
            box.Text = "";
            Assert.False(popup.IsOpen);

            void Press(Key key) => box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(box), 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        }
        finally
        {
            window.Close();
            ThemeService.Apply(AppTheme.Dark);
        }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static PlayRecord Play(string track, string artist, int ms) => new()
    {
        TrackName = track,
        ArtistName = artist,
        AlbumName = "Album",
        MsPlayed = ms,
    };
}
