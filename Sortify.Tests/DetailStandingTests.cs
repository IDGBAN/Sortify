using System.Windows;
using System.Windows.Threading;
using Sortify.Models;
using Sortify.Services;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>Rank, share, skips, busiest month, streak, device and plays in a breakdown.</summary>
public class DetailStandingTests
{
    private static PlayRecord Play(string track, string artist, DateTime ts, int ms = 200_000, bool skipped = false,
        string platform = "android OS 14 API 34 (Google, Pixel 8)", string album = "Album") => new()
        {
            TrackName = track,
            ArtistName = artist,
            AlbumName = album,
            MsPlayed = ms,
            Timestamp = ts,
            Skipped = skipped,
            Platform = platform,
        };

    private static readonly DateTime Start = new(2024, 3, 1, 12, 0, 0);

    private static List<PlayRecord> Library() => new()
    {
        Play("A1", "Alpha", Start),
        Play("A1", "Alpha", Start.AddDays(1)),
        Play("A2", "Alpha", Start.AddDays(2), platform: "windows 10 (10.0.22631; x64)"),
        Play("A2", "Alpha", Start.AddDays(2).AddHours(1), ms: 2_000, skipped: true),
        Play("A1", "Alpha", Start.AddDays(40)),
        Play("B1", "Beta", Start.AddDays(5), ms: 900_000),
        Play("C1", "Gamma", Start.AddDays(6), ms: 60_000),
    };

    private static DetailResult Alpha(List<PlayRecord> records, AnalysisResult? overall = null) =>
        DetailEngine.Build(records, new FilterOptions(), DetailScope.Artist, "Alpha", string.Empty, overall);

    [Fact]
    public void Skips_CountTheShortPlays_ThatThePlayCountLeavesOut()
    {
        var d = Alpha(Library());

        Assert.Equal(4, d.PlayCount);
        Assert.Equal(5, d.SkipEligiblePlays);
        Assert.Equal(1, d.Skips);
        Assert.Equal(20, d.SkipRate, precision: 6);
    }

    [Fact]
    public void TheBusiestMonth_IsTheOneWithTheMostListening()
    {
        var d = Alpha(Library());

        Assert.Equal(new DateTime(2024, 3, 1), d.BusiestMonth);
        Assert.Equal(600_000, d.BusiestMonthMs);
    }

    [Fact]
    public void TheLongestStreak_RunsOverConsecutiveDays()
    {
        var d = Alpha(Library());

        Assert.Equal(3, d.LongestStreakDays);
        Assert.Equal(Start.Date, d.LongestStreakStart);
        Assert.Equal(Start.Date.AddDays(2), d.LongestStreakEnd);
    }

    [Fact]
    public void TheTopDevice_IsTheFamilyListenedOnMost()
    {
        var d = Alpha(Library());

        Assert.Equal("Mobile", d.TopDevice);
        Assert.Equal(75, d.TopDevicePercent, precision: 6);
    }

    [Fact]
    public void ThePlays_AreEveryCountedPlay_OldestFirst()
    {
        var records = Library();
        records.Reverse();

        var d = Alpha(records);

        Assert.Equal(d.PlayCount, d.Plays.Count);
        Assert.Equal(d.Plays.OrderBy(p => p.Timestamp), d.Plays);
        Assert.DoesNotContain(d.Plays, p => p.MsPlayed < FilterOptions.DefaultMinMs);
    }

    [Fact]
    public void RankAndShare_ComeFromTheResultsItWasOpenedFrom()
    {
        var records = Library();
        var overall = AnalysisEngine.Analyze(records, new FilterOptions());

        var alpha = Alpha(records, overall);
        var gamma = DetailEngine.Build(records, new FilterOptions(), DetailScope.Artist, "Gamma", string.Empty, overall);

        Assert.Equal(2, alpha.Rank);
        Assert.Equal(3, alpha.RankOutOf);
        Assert.Equal(800_000 * 100.0 / 1_760_000, alpha.SharePercent, precision: 6);
        Assert.Equal(3, gamma.Rank);
    }

    [Fact]
    public void ByPlays_TheRankShareAndBusiestMonth_CountPlays()
    {
        var records = Library();
        var overall = AnalysisEngine.Analyze(records, new FilterOptions());

        var alpha = DetailEngine.Build(records, new FilterOptions(), DetailScope.Artist, "Alpha", string.Empty, overall, Metric.Plays);

        // Second by time behind Beta's one long play, but first by plays: 4 of the 6.
        Assert.Equal(1, alpha.Rank);
        Assert.Equal(4 * 100.0 / 6, alpha.SharePercent, precision: 6);
        Assert.Equal(new DateTime(2024, 3, 1), alpha.BusiestMonth);
        Assert.Equal(3, alpha.BusiestMonthPlays);
    }

    [Fact]
    public void ByPlays_ATie_GoesToTheLongerListen()
    {
        // March and May have two plays each, and May the longer listen. In May, the 10th and
        // the 11th have one play each, and the 11th the longer one.
        var may = new DateTime(2024, 5, 10, 12, 0, 0);
        var records = new List<PlayRecord>
        {
            Play("A1", "Alpha", Start),
            Play("A1", "Alpha", Start.AddHours(1)),
            Play("A1", "Alpha", may, ms: 300_000),
            Play("A1", "Alpha", may.AddDays(1), ms: 500_000),
        };
        var overall = AnalysisEngine.Analyze(records, new FilterOptions());
        var filter = new FilterOptions();

        var alpha = DetailEngine.Build(records, filter, DetailScope.Artist, "Alpha", string.Empty, overall, Metric.Plays);
        Assert.Equal(new DateTime(2024, 5, 1), alpha.BusiestMonth);
        Assert.Equal(2, alpha.BusiestMonthPlays);

        var tenth = DetailEngine.Build(records, filter, DetailScope.Day, "2024-05-10", string.Empty, overall, Metric.Plays);
        var eleventh = DetailEngine.Build(records, filter, DetailScope.Day, "2024-05-11", string.Empty, overall, Metric.Plays);
        Assert.Equal(3, tenth.Rank);
        Assert.Equal(2, eleventh.Rank);
        Assert.Equal(3, eleventh.RankOutOf);
    }

    [Fact]
    public void TracksAlbumsAndYears_AreRankedAmongTheirOwnKind()
    {
        var records = Library();
        records.Add(Play("Old", "Delta", new DateTime(2023, 6, 1, 9, 0, 0), album: "Older"));
        var overall = AnalysisEngine.Analyze(records, new FilterOptions());
        var filter = new FilterOptions();

        Assert.Equal(1, DetailEngine.Build(records, filter, DetailScope.Track, "B1", "Beta", overall).Rank);
        Assert.Equal(2, DetailEngine.Build(records, filter, DetailScope.Track, "A1", "Alpha", overall).Rank);
        Assert.Equal(1, DetailEngine.Build(records, filter, DetailScope.Album, "Album", "Beta", overall).Rank);

        var older = DetailEngine.Build(records, filter, DetailScope.Year, "2023", string.Empty, overall);
        Assert.Equal(2, older.Rank);
        Assert.Equal(2, older.RankOutOf);
    }

    [Fact]
    public void AShow_IsRankedAmongShows_AndSharesThePodcastListening()
    {
        var records = new List<PlayRecord>
        {
            new() { Kind = ContentKind.Podcast, ShowName = "Big", EpisodeName = "1", MsPlayed = 3_000_000, Timestamp = Start },
            new() { Kind = ContentKind.Podcast, ShowName = "Small", EpisodeName = "1", MsPlayed = 1_000_000, Timestamp = Start },
            Play("Song", "Band", Start),
        };
        var overall = AnalysisEngine.Analyze(records, new FilterOptions());

        var small = DetailEngine.Build(records, new FilterOptions(), DetailScope.Show, "Small", string.Empty, overall);

        Assert.Equal(2, small.Rank);
        Assert.Equal(25, small.SharePercent, precision: 6);
    }

    [Fact]
    public void WithoutTheOverallResults_NothingIsRanked()
    {
        var d = Alpha(Library());

        Assert.Null(d.Rank);
        Assert.Equal(0, d.SharePercent);
    }
}

/// <summary>The breakdown window showing those figures.</summary>
[Collection(WpfCollection.Name)]
public class DetailStandingWindowTests
{
    private static List<PlayRecord> Records() => new()
    {
        new() { TrackName = "One", ArtistName = "Band", AlbumName = "LP", MsPlayed = 200_000, Timestamp = new DateTime(2024, 1, 1, 9, 0, 0), ReasonEnd = "trackdone" },
        new() { TrackName = "Two", ArtistName = "Band", AlbumName = "LP", MsPlayed = 100_000, Timestamp = new DateTime(2024, 1, 2, 9, 0, 0), ReasonEnd = "fwdbtn", Skipped = true },
    };

    [Fact]
    public void AnArtist_ListsEveryPlay_WithTheTrackButNotTheArtist()
    {
        WpfTestHost.Run(() =>
        {
            var records = Records();
            var d = DetailEngine.Build(records, new FilterOptions(), DetailScope.Artist, "Band", string.Empty,
                AnalysisEngine.Analyze(records, new FilterOptions()));
            var window = new DetailWindow(d);

            Assert.Equal(2, window.PlaysGrid.Items.Count);
            Assert.Equal(Visibility.Visible, window.PlaysGrid.Columns[1].Visibility);
            Assert.Equal(Visibility.Collapsed, window.PlaysGrid.Columns[2].Visibility);
            Assert.Equal("#1", window.RankText.Text);
            Assert.Equal("of 1 artist", window.RankHint.Text);
            Assert.Equal("50%", window.SkipText.Text);
        });
    }

    [Fact]
    public void ATrack_LeavesTheTrackColumnOut_AndNoPlaysHidesTheList()
    {
        WpfTestHost.Run(() =>
        {
            var d = DetailEngine.Build(Records(), new FilterOptions(), DetailScope.Track, "One", "Band");
            var window = new DetailWindow(d);

            Assert.Equal(Visibility.Collapsed, window.PlaysGrid.Columns[1].Visibility);
            Assert.Equal("-", window.RankText.Text);

            var empty = new DetailWindow(new DetailResult { Scope = DetailScope.Artist, Title = "Nobody" });
            Assert.Equal(Visibility.Collapsed, empty.PlaysCard.Visibility);
        });
    }

    [Fact]
    public async Task ANarrowWindow_PutsTheFiguresInTwoRows()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var records = Records();
            var day = DetailEngine.Build(records, new FilterOptions(), DetailScope.Day, "2024-01-01", string.Empty,
                AnalysisEngine.Analyze(records, new FilterOptions()));
            var window = new DetailWindow(day)
            {
                Width = 1080,
                Left = -10000,
                Top = 0,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowActivated = false,
                ShowInTaskbar = false,
            };
            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(5, window.SummaryCards.Columns);
                // A day has no busiest month or streak, so four figures.
                Assert.Equal(4, window.StandingCards.Columns);

                window.Width = 700;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(3, window.SummaryCards.Columns);
                Assert.Equal(2, window.StandingCards.Columns);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
