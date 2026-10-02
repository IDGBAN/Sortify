using System.Windows.Documents;
using Sortify.Models;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>Opening a track, album or artist from inside another breakdown.</summary>
[Collection(WpfCollection.Name)]
public class DetailDrillTests
{
    [Fact]
    public void EachRowOpensItsOwnKindOfBreakdown()
    {
        Assert.Equal((DetailScope.Track, "Song", "Band"), DetailWindow.TargetFor(new TrackStat { Track = "Song", Artist = "Band" }));
        Assert.Equal((DetailScope.Album, "Record", "Band"), DetailWindow.TargetFor(new AlbumStat { Album = "Record", Artist = "Band" }));
        Assert.Equal((DetailScope.Artist, "Band", ""), DetailWindow.TargetFor(new ArtistStat { Artist = "Band" }));
        Assert.Equal((DetailScope.Year, "2024", ""), DetailWindow.TargetFor(new YearStat { Year = 2024 }));
        Assert.Null(DetailWindow.TargetFor("something else"));
    }

    private static DetailResult TrackDetail() => new()
    {
        Scope = DetailScope.Track,
        Title = "Song",
        Subtitle = "Band",
        TotalMsPlayed = 120_000,
        PlayCount = 2,
    };

    private static IEnumerable<Inline> SubtitleInlines(DetailWindow window) =>
        ((System.Windows.Controls.TextBlock)window.FindName("SubtitleText")).Inlines;

    [Fact]
    public void TheArtistUnderATrackIsALink_WhenItCanBeOpened()
    {
        WpfTestHost.Run(() =>
        {
            var window = new DetailWindow(TrackDetail(), (_, _, _) => Task.FromResult<DetailResult?>(null));

            var link = Assert.Single(SubtitleInlines(window).OfType<Hyperlink>());
            Assert.Equal("Band", Assert.Single(link.Inlines.OfType<Run>()).Text);
        });
    }

    [Fact]
    public void TheArtistIsPlainText_WhenNothingCanBeOpened()
    {
        WpfTestHost.Run(() =>
        {
            var window = new DetailWindow(TrackDetail());

            Assert.Empty(SubtitleInlines(window).OfType<Hyperlink>());
            Assert.Equal("Track - Band", string.Concat(SubtitleInlines(window).OfType<Run>().Select(r => r.Text)));
        });
    }
}
