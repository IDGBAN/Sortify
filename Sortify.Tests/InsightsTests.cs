using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Xunit;

namespace Sortify.Tests;

/// <summary>Covers the statistics added alongside the streaks and sessions.</summary>
public class InsightsTests
{
    private static readonly FilterOptions NoFilter = new() { MinMsPlayed = 0 };

    private static PlayRecord Record(
        string track = "Track", string artist = "Artist", string album = "Album",
        int ms = 60_000, DateTime? ts = null, string? reasonEnd = null)
        => new()
        {
            TrackName = track,
            ArtistName = artist,
            AlbumName = album,
            MsPlayed = ms,
            Timestamp = ts ?? new DateTime(2023, 5, 10, 14, 0, 0),
            ReasonEnd = reasonEnd,
        };

    [Fact]
    public void PeakHour_IsTheBusiestCellOfTheWeek_NotTheBusiestHourAndDaySeparately()
    {
        // Friday wins as a day and 21:00 wins as an hour, but nothing played on Friday at 21:00.
        var records = new[]
        {
            Record(ms: 100 * 60_000, ts: new DateTime(2023, 5, 8, 21, 30, 0)),  // Monday
            Record(ms: 60 * 60_000, ts: new DateTime(2023, 5, 12, 9, 30, 0)),   // Friday
            Record(ms: 60 * 60_000, ts: new DateTime(2023, 5, 12, 10, 30, 0)),  // Friday
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal("21:00-22:00 on Mondays", MainViewModel.BuildPeakHourText(r));
    }

    [Fact]
    public void PeakHour_IsBlank_WhenNoPlayCarriesATimestamp()
    {
        var r = AnalysisEngine.Analyze(new[] { Record(ts: DateTime.MinValue) }, NoFilter);

        Assert.Equal("-", MainViewModel.BuildPeakHourText(r));
    }

    [Fact]
    public void LongestBreak_CountsTheSilentDaysBetweenTwoActiveDays()
    {
        var records = new[]
        {
            Record(ts: new DateTime(2023, 1, 1, 12, 0, 0)),
            Record(ts: new DateTime(2023, 1, 2, 12, 0, 0)),   // no gap
            Record(ts: new DateTime(2023, 1, 12, 12, 0, 0)),  // 9 silent days
            Record(ts: new DateTime(2023, 1, 15, 12, 0, 0)),  // 2 silent days
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal(9, r.LongestBreakDays);
        Assert.Equal(new DateTime(2023, 1, 2), r.LongestBreakStart);
        Assert.Equal(new DateTime(2023, 1, 12), r.LongestBreakEnd);
    }

    [Fact]
    public void LongestBreak_IsZeroWhenEveryDayIsConsecutive()
    {
        var records = new[]
        {
            Record(ts: new DateTime(2023, 1, 1, 12, 0, 0)),
            Record(ts: new DateTime(2023, 1, 2, 12, 0, 0)),
            Record(ts: new DateTime(2023, 1, 3, 12, 0, 0)),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal(0, r.LongestBreakDays);
        Assert.Null(r.LongestBreakStart);
    }

    [Fact]
    public void LongestBreak_IsZeroForASingleDay()
    {
        var r = AnalysisEngine.Analyze(new[] { Record() }, NoFilter);

        Assert.Equal(0, r.LongestBreakDays);
    }

    [Fact]
    public void CompletedPlays_CountsOnlyTrackdone()
    {
        var records = new[]
        {
            Record(reasonEnd: "trackdone"),
            Record(reasonEnd: "TRACKDONE"),   // Spotify's casing is not guaranteed
            Record(reasonEnd: "fwdbtn"),
            Record(reasonEnd: null),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal(2, r.CompletedPlays);
        Assert.Equal(4, r.SkipEligiblePlays);
    }

    [Fact]
    public void CompletedPlays_IncludesPlaysUnderTheDurationCutoff()
    {
        // Completion rate is the mirror of skip rate, which deliberately relaxes the cutoff.
        var records = new[]
        {
            Record(ms: 1_000, reasonEnd: "trackdone"),
            Record(ms: 90_000, reasonEnd: "trackdone"),
        };

        var r = AnalysisEngine.Analyze(records, new FilterOptions { MinMsPlayed = 5_000 });

        Assert.Equal(2, r.CompletedPlays);
        Assert.Equal(1, r.TotalPlays);   // only the long one counts toward listening
    }

    [Fact]
    public void PlaysPerTrack_MeasuresRepetition()
    {
        var records = new[]
        {
            Record(track: "A"), Record(track: "A"), Record(track: "A"),
            Record(track: "B"),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal(2, r.UniqueTracks);
        Assert.Equal(2.0, r.PlaysPerTrack, 3);
    }

    [Fact]
    public void PlaysPerTrack_IsZeroWithNoTracks()
    {
        Assert.Equal(0, AnalysisEngine.Analyze(Array.Empty<PlayRecord>(), NoFilter).PlaysPerTrack);
    }

    [Fact]
    public void TopArtistShare_IsAPercentageOfTotalTime()
    {
        var records = new[]
        {
            Record(artist: "X", ms: 75_000),
            Record(artist: "Y", ms: 25_000),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal("X", r.Artists[0].Artist);
        Assert.Equal(75.0, r.TopArtistSharePercent, 3);
    }

    [Fact]
    public void TopArtistShare_IsZeroWithNothingLoaded()
    {
        Assert.Equal(0, AnalysisResult.Empty.TopArtistSharePercent);
    }

    [Fact]
    public void NewArtistsPerMonth_AveragesOverTheMonthsThatHadDiscoveries()
    {
        var records = new[]
        {
            Record(artist: "A", ts: new DateTime(2023, 1, 5, 12, 0, 0)),
            Record(artist: "B", ts: new DateTime(2023, 1, 6, 12, 0, 0)),
            Record(artist: "C", ts: new DateTime(2023, 2, 5, 12, 0, 0)),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        // Two artists discovered in January, one in February.
        Assert.Equal(2, r.NewArtistsByMonth.Count);
        Assert.Equal(1.5, r.NewArtistsPerMonth, 3);
    }

    [Fact]
    public void SessionGap_IsConfigurable()
    {
        var records = new[]
        {
            Record(ms: 60_000, ts: new DateTime(2023, 1, 1, 12, 0, 0)),
            // Starts 20 minutes after the first play ended.
            Record(ms: 60_000, ts: new DateTime(2023, 1, 1, 12, 21, 0)),
        };

        var oneSession = AnalysisEngine.Analyze(records, NoFilter, TimeSpan.FromMinutes(30));
        var twoSessions = AnalysisEngine.Analyze(records, NoFilter, TimeSpan.FromMinutes(5));

        Assert.Equal(1, oneSession.SessionCount);
        Assert.Equal(2, twoSessions.SessionCount);
    }

    [Fact]
    public void SessionGap_DefaultsToThirtyMinutes()
    {
        var records = new[]
        {
            Record(ms: 60_000, ts: new DateTime(2023, 1, 1, 12, 0, 0)),
            Record(ms: 60_000, ts: new DateTime(2023, 1, 1, 12, 21, 0)),
        };

        Assert.Equal(1, AnalysisEngine.Analyze(records, NoFilter).SessionCount);
    }
}
