using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Xunit;

namespace Sortify.Tests;

/// <summary>Copying tracks as links that paste into a Spotify playlist.</summary>
public class SpotifyLinkTests
{
    private static TrackStat Track(string name, string uri) => new() { Track = name, Artist = "Artist", Uri = uri };

    [Theory]
    [InlineData("spotify:track:4uLU6hMCjMI75M1A2tKUQC", "https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC")]
    [InlineData("spotify:episode:abc", "https://open.spotify.com/episode/abc")]
    [InlineData("SPOTIFY:track:abc", "https://open.spotify.com/track/abc")]
    [InlineData("spotify:track:a b&c", "https://open.spotify.com/track/a%20b%26c")]
    [InlineData("spotify:track:", "")]
    [InlineData("spotify:local:Artist:Album:Title:215", "")]
    [InlineData("https://example.com", "")]
    [InlineData("", "")]
    public void FromUri_BuildsAnOpenSpotifyLink(string uri, string expected)
    {
        Assert.Equal(expected, SpotifyLink.FromUri(uri));
    }

    [Fact]
    public void ForTracks_PutsOneLinkPerLineInOrderWithoutRepeats()
    {
        var links = SpotifyLink.ForTracks(new[]
        {
            Track("One", "spotify:track:1"),
            Track("Old", ""),
            Track("Two", "spotify:track:2"),
            Track("One again", "spotify:track:1"),
        });

        Assert.Equal("https://open.spotify.com/track/1\nhttps://open.spotify.com/track/2", links.Text);
        Assert.Equal(2, links.Copied);
        Assert.Equal(1, links.Missing);
    }

    [Fact]
    public void Analysis_KeepsAUriForEachTrack()
    {
        var records = new[]
        {
            new PlayRecord { TrackName = "Song", ArtistName = "A", MsPlayed = 60_000, Timestamp = new DateTime(2023, 1, 1, 10, 0, 0) },
            new PlayRecord { TrackName = "Song", ArtistName = "A", MsPlayed = 60_000, Timestamp = new DateTime(2023, 1, 2, 10, 0, 0), Uri = "spotify:track:xyz" },
        };

        var result = AnalysisEngine.Analyze(records, new FilterOptions());

        Assert.Equal("spotify:track:xyz", Assert.Single(result.Tracks).Uri);
    }

    [Fact]
    public void ExcludingSeveralRows_IsOneChange()
    {
        var vm = new FilterViewModel();
        int changes = 0;
        vm.FiltersChanged += (_, _) => changes++;

        vm.ExcludeArtists(new[] { "A", "B", "a" });

        Assert.Equal(1, changes);
        Assert.Equal(new[] { "A", "B" }, vm.ExcludedArtists);
    }
}

[Collection(WpfCollection.Name)]
public class CopyLinksStatusTests
{
    private static string Copy(IEnumerable<TrackStat> tracks, bool clipboardWorks, out string? copied)
    {
        string? text = null;
        string status = string.Empty;
        WpfTestHost.Run(() =>
        {
            var vm = new MainViewModel(new AppSettings());
            vm.CopySpotifyLinks(tracks, t =>
            {
                text = t;
                return clipboardWorks;
            });
            status = vm.StatusText;
        });
        copied = text;
        return status;
    }

    [Fact]
    public void SaysHowManyWereCopiedAndWhy()
    {
        var status = Copy(new[]
        {
            new TrackStat { Track = "One", Artist = "A", Uri = "spotify:track:1" },
            new TrackStat { Track = "Old", Artist = "A" },
        }, clipboardWorks: true, out var copied);

        Assert.Equal("https://open.spotify.com/track/1", copied);
        Assert.Equal("Copied 1 Spotify link. Paste it into a playlist in the Spotify desktop app. " +
                     "1 track has no link in the export and was left out.", status);
    }

    [Fact]
    public void ExplainsWhenNoTrackHasALink()
    {
        var status = Copy(new[] { new TrackStat { Track = "Old", Artist = "A" } }, clipboardWorks: true, out var copied);

        Assert.Null(copied);
        Assert.Contains("Only the extended streaming history records them", status);
    }

    [Fact]
    public void SaysSoWhenTheClipboardIsBusy()
    {
        var status = Copy(new[] { new TrackStat { Track = "One", Artist = "A", Uri = "spotify:track:1" } },
            clipboardWorks: false, out _);

        Assert.Contains("another program is holding the clipboard", status);
    }
}
