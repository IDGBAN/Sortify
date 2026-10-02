using Sortify.Models;
using Sortify.Services;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>The plays that mark a history.</summary>
public class MilestoneTests
{
    private static readonly DateTime Start = new(2023, 1, 1, 8, 0, 0);

    // One play an hour, each named after its position, so a milestone names the play it landed on.
    private static List<PlayRecord> Plays(int count, int minutes = 3, string artist = "Band") =>
        Enumerable.Range(1, count).Select(i => new PlayRecord
        {
            TrackName = $"Play {i}",
            ArtistName = artist,
            AlbumName = "Album",
            MsPlayed = minutes * 60_000,
            Timestamp = Start.AddHours(i),
        }).ToList();

    private static IReadOnlyList<Milestone> MilestonesOf(IEnumerable<PlayRecord> records) =>
        AnalysisEngine.Analyze(records.ToList(), new FilterOptions()).Milestones;

    [Fact]
    public void TheFirstPlayIsAlwaysOne()
    {
        var first = MilestonesOf(Plays(3)).First();

        Assert.Equal("First play", first.Label);
        Assert.Equal("Play 1", first.Track.Track);
        Assert.Equal(Start.AddHours(1), first.Date);
    }

    [Fact]
    public void TheThousandthPlayLandsOnTheThousandthPlay()
    {
        var milestone = MilestonesOf(Plays(1_200)).Single(m => m.Label == "1,000th play");

        Assert.Equal("Play 1000", milestone.Track.Track);
    }

    [Fact]
    public void AnHourMarkLandsOnThePlayThatCrossedIt()
    {
        // Ten-minute plays: the 600th takes the total to exactly 100 hours.
        var milestone = MilestonesOf(Plays(700, minutes: 10)).Single(m => m.Label == "100 hours listened");

        Assert.Equal("Play 600", milestone.Track.Track);
    }

    [Fact]
    public void OnePlayCanPassSeveralMarks()
    {
        var records = new List<PlayRecord>
        {
            new() { TrackName = "Marathon", ArtistName = "Band", MsPlayed = int.MaxValue, Timestamp = Start },
        };

        var labels = MilestonesOf(records).Select(m => m.Label).ToList();

        Assert.Contains("100 hours listened", labels);
        Assert.Contains("500 hours listened", labels);
        Assert.DoesNotContain("1,000 hours listened", labels);
    }

    [Fact]
    public void MarksNotReachedAreLeftOut()
    {
        Assert.DoesNotContain(MilestonesOf(Plays(500)), m => m.Label == "1,000th play");
    }

    [Fact]
    public void TheTopArtistsFirstPlayIsMarked_AndEverythingIsInDateOrder()
    {
        var records = Plays(5, artist: "Early Band");
        records.AddRange(Plays(30, minutes: 30, artist: "Top Band").Select(r => new PlayRecord
        {
            TrackName = "Big Song",
            ArtistName = r.ArtistName,
            AlbumName = r.AlbumName,
            MsPlayed = r.MsPlayed,
            Timestamp = r.Timestamp.AddDays(10),
        }));

        var milestones = MilestonesOf(records);

        var top = milestones.Single(m => m.Label.StartsWith("First time hearing"));
        Assert.Equal("First time hearing Top Band, your top artist", top.Label);
        Assert.Equal("Big Song", top.Track.Track);
        Assert.Equal(milestones.OrderBy(m => m.Date).Select(m => m.Label), milestones.Select(m => m.Label));
    }

    [Fact]
    public void NoDatedPlaysMeansNoMilestones()
    {
        var records = new[] { new PlayRecord { TrackName = "Song", ArtistName = "Band", MsPlayed = 60_000 } };

        Assert.Empty(MilestonesOf(records));
    }

    [Fact]
    public void SessionsAreUnchangedByTheSharedSort()
    {
        // Out of order on purpose: sessions read the plays in time order whatever the input order.
        var records = Plays(4, minutes: 50);
        records.Reverse();

        var result = AnalysisEngine.Analyze(records, new FilterOptions());

        Assert.Equal(1, result.SessionCount);
        Assert.Equal(200 * 60_000, result.LongestSessionMs);
    }

    [Fact]
    public void AMilestoneOpensItsTrack()
    {
        var milestone = new Milestone("First play", Start, new TrackStat { Track = "Song", Artist = "Band" });

        Assert.Equal((DetailScope.Track, "Song", "Band"), DetailWindow.TargetFor(milestone));
    }
}
