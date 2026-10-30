using System.IO;
using System.Text.Json;
using Sortify.Models;
using Sortify.Services;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>The tracks that were put on repeat.</summary>
public class OnRepeatTests
{
    private static readonly DateTime Start = new(2024, 3, 1, 12, 0, 0);

    private static PlayRecord Play(string track, DateTime ts, int ms = 200_000, string artist = "Band") => new()
    {
        TrackName = track,
        ArtistName = artist,
        AlbumName = "Album",
        MsPlayed = ms,
        Timestamp = ts,
    };

    /// <summary><paramref name="count"/> plays of a track, <paramref name="hoursApart"/> hours apart.</summary>
    private static IEnumerable<PlayRecord> Burst(string track, DateTime from, int count, double hoursApart, string artist = "Band") =>
        Enumerable.Range(0, count).Select(i => Play(track, from.AddHours(i * hoursApart), artist: artist));

    private static AnalysisResult Analyze(IEnumerable<PlayRecord> records) =>
        AnalysisEngine.Analyze(records.ToList(), new FilterOptions());

    [Fact]
    public void ATrackPlayedEnoughWithinTheWindow_IsListedAtItsBusiestStretch()
    {
        var records = Burst("Loop", Start, 12, hoursApart: 12)
            .Concat(Burst("Loop", Start.AddDays(30), 3, hoursApart: 24));

        var repeat = Assert.Single(Analyze(records).OnRepeat);

        Assert.Equal("Loop", repeat.Track.Track);
        Assert.Equal(12, repeat.Plays);
        Assert.Equal(Start, repeat.From);
        Assert.Equal(Start.AddHours(11 * 12), repeat.To);
        Assert.Equal(15, repeat.Track.PlayCount);
    }

    [Fact]
    public void PlaysAWholeWindowApart_DoNotCountTogether()
    {
        var window = TimeSpan.FromDays(AnalysisEngine.RepeatWindowDays);
        double hoursApart = window.TotalHours / (AnalysisEngine.RepeatMinPlays - 1);

        // Just enough plays, but the first and last are exactly a window apart.
        var records = Burst("Spread", Start, AnalysisEngine.RepeatMinPlays, hoursApart);

        Assert.Empty(Analyze(records).OnRepeat);
    }

    [Fact]
    public void TooFewPlays_LeaveATrackOff()
    {
        var records = Burst("Almost", Start, AnalysisEngine.RepeatMinPlays - 1, hoursApart: 1);

        Assert.Empty(Analyze(records).OnRepeat);
    }

    [Fact]
    public void ShortPlays_DoNotCount()
    {
        var records = Burst("Full", Start, AnalysisEngine.RepeatMinPlays - 1, hoursApart: 1)
            .Append(Play("Full", Start.AddHours(20), ms: 2_000));

        Assert.Empty(Analyze(records).OnRepeat);
    }

    [Fact]
    public void TheList_PutsTheMostPlaysFirst_AndTheEarlierStretchOnATie()
    {
        var records = Burst("Ten", Start, 10, hoursApart: 1)
            .Concat(Burst("Fifteen", Start.AddDays(40), 15, hoursApart: 1, artist: "Other"))
            .Concat(Burst("Also Ten", Start.AddDays(-20), 10, hoursApart: 1, artist: "Third"));

        var list = Analyze(records).OnRepeat;

        Assert.Equal(new[] { "Fifteen", "Also Ten", "Ten" }, list.Select(r => r.Track.Track));
    }

    [Fact]
    public void TheList_StopsAtItsLimit()
    {
        var records = Enumerable.Range(0, AnalysisEngine.RepeatRows + 5)
            .SelectMany(i => Burst($"Song {i}", Start.AddDays(i * 10), 10, hoursApart: 1, artist: $"Band {i}"));

        Assert.Equal(AnalysisEngine.RepeatRows, Analyze(records).OnRepeat.Count);
    }

    [Fact]
    public void ARepeat_OpensItsTrack()
    {
        var repeat = new RepeatStat(new TrackStat { Track = "Loop", Artist = "Band" }, 12, Start, Start.AddDays(2));

        Assert.Equal((DetailScope.Track, "Loop", "Band"), DetailWindow.TargetFor(repeat));
    }

    [Fact]
    public async Task TheReports_IncludeTheList()
    {
        var result = Analyze(Burst("Loop", Start, 12, hoursApart: 2));
        var dir = Path.Combine(Path.GetTempPath(), "SortifyRepeat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            await ExportService.SaveJsonAsync(Path.Combine(dir, "r.json"), result);
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dir, "r.json")));
            var repeat = doc.RootElement.GetProperty("onRepeat")[0];
            Assert.Equal("Loop", repeat.GetProperty("track").GetString());
            Assert.Equal(12, repeat.GetProperty("plays").GetInt32());

            await ExportService.SaveMarkdownAsync(Path.Combine(dir, "r.md"), result);
            var markdown = await File.ReadAllTextAsync(Path.Combine(dir, "r.md"));
            Assert.Contains("## On repeat", markdown);
            Assert.Contains("| Loop | Band | 12 | 2024-03-01 | 2024-03-02 |", markdown);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
