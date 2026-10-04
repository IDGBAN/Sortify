using System.IO;
using System.Text.Json;
using Sortify.Models;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

/// <summary>The figures the Tracks, Artists and Albums tables show beyond time and plays.</summary>
public class TableColumnsTests
{
    private static PlayRecord Play(string track, string artist = "X", string album = "Album", int ms = 200_000,
        DateTime? ts = null, bool skipped = false) => new()
        {
            TrackName = track,
            ArtistName = artist,
            AlbumName = album,
            MsPlayed = ms,
            Timestamp = ts ?? new DateTime(2024, 1, 10, 12, 0, 0),
            Skipped = skipped,
        };

    [Fact]
    public void SkipRate_CountsTheShortPlaysTheCutoffLeavesOut()
    {
        var records = new[]
        {
            Play("A"),
            Play("A"),
            Play("A"),
            Play("A", ms: 2_000, skipped: true),
            Play("B"),
        };

        var r = AnalysisEngine.Analyze(records, new FilterOptions());
        var a = r.Tracks.Single(t => t.Track == "A");

        Assert.Equal(3, a.PlayCount);
        Assert.Equal(25, a.SkipRate, precision: 6);
        Assert.Equal(0, r.Tracks.Single(t => t.Track == "B").SkipRate);
    }

    [Fact]
    public void Artists_CountTheirTracksAndTheMonthsTheyWerePlayed()
    {
        var records = new[]
        {
            Play("A", ts: new DateTime(2024, 1, 3)),
            Play("A", ts: new DateTime(2024, 1, 20)),
            Play("B", ts: new DateTime(2024, 3, 2)),
            Play("C", ts: new DateTime(2025, 1, 2)),
            Play("Other", artist: "Y", ts: new DateTime(2024, 1, 5)),
        };

        var r = AnalysisEngine.Analyze(records, new FilterOptions());
        var x = r.Artists.Single(a => a.Artist == "X");
        var y = r.Artists.Single(a => a.Artist == "Y");

        Assert.Equal(3, x.TrackCount);
        Assert.Equal(3, x.MonthCount);
        Assert.Equal(1, y.TrackCount);
        Assert.Equal(1, y.MonthCount);
    }

    [Fact]
    public void UndatedPlays_CountTowardTracksButNoMonth()
    {
        var r = AnalysisEngine.Analyze(new[] { Play("A", ts: DateTime.MinValue) }, new FilterOptions());

        Assert.Equal(1, r.Artists[0].TrackCount);
        Assert.Equal(0, r.Artists[0].MonthCount);
    }

    [Fact]
    public void Albums_CountTheirOwnTracks_EvenWhenATrackIsOnTwoOfThem()
    {
        var records = new[]
        {
            Play("A", album: "First"),
            Play("A", album: "First"),
            Play("B", album: "First"),
            Play("A", album: "Best Of"),
        };

        var r = AnalysisEngine.Analyze(records, new FilterOptions());

        Assert.Equal(2, r.Albums.Single(a => a.Album == "First").TrackCount);
        Assert.Equal(1, r.Albums.Single(a => a.Album == "Best Of").TrackCount);
        Assert.Equal(2, r.Artists[0].TrackCount);
    }

    [Fact]
    public async Task TheJsonExport_CarriesTheNewFigures()
    {
        var r = AnalysisEngine.Analyze(new[] { Play("A"), Play("A", ms: 1_000, skipped: true) }, new FilterOptions());
        var path = Path.Combine(Path.GetTempPath(), "SortifyColumns_" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            await ExportService.SaveJsonAsync(path, r);
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var root = doc.RootElement;

            Assert.Equal(50, root.GetProperty("tracks")[0].GetProperty("skipRatePercent").GetDouble());
            Assert.Equal(1, root.GetProperty("artists")[0].GetProperty("tracks").GetInt32());
            Assert.Equal(1, root.GetProperty("artists")[0].GetProperty("months").GetInt32());
            Assert.Equal(1, root.GetProperty("albums")[0].GetProperty("tracks").GetInt32());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
