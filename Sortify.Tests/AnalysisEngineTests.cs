using Sortify.Models;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

public class AnalysisEngineTests
{
    private static readonly FilterOptions NoFilter = new() { MinMsPlayed = 0 };

    private static PlayRecord Record(
        string track = "Track", string artist = "Artist", string album = "Album",
        int ms = 60_000, DateTime? ts = null, bool skipped = false, string? reasonEnd = null)
        => new()
        {
            TrackName = track,
            ArtistName = artist,
            AlbumName = album,
            MsPlayed = ms,
            Timestamp = ts ?? new DateTime(2023, 5, 10, 14, 0, 0),
            Skipped = skipped,
            ReasonEnd = reasonEnd,
        };

    [Fact]
    public void Aggregates_TotalsAndUniqueCounts()
    {
        var records = new[]
        {
            Record(track: "A", artist: "X", ms: 100_000),
            Record(track: "A", artist: "X", ms: 50_000),
            Record(track: "B", artist: "Y", ms: 25_000),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal(3, r.TotalPlays);
        Assert.Equal(175_000, r.TotalMsPlayed);
        Assert.Equal(2, r.UniqueTracks);
        Assert.Equal(2, r.UniqueArtists);
        Assert.Equal("X", r.Artists[0].Artist);            // most listened first
        Assert.Equal(150_000, r.Artists[0].TotalMsPlayed);
        Assert.Equal(2, r.Artists[0].PlayCount);
    }

    [Fact]
    public void SameTrackName_DifferentArtists_AreSeparateTracks()
    {
        var records = new[]
        {
            Record(track: "Intro", artist: "X"),
            Record(track: "Intro", artist: "Y"),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal(2, r.UniqueTracks);
    }

    [Fact]
    public void ByPlayCountViews_AreSortedByPlays()
    {
        var records = new[]
        {
            Record(track: "Long", artist: "X", ms: 500_000),
            Record(track: "Short", artist: "Y", ms: 10_000),
            Record(track: "Short", artist: "Y", ms: 10_000),
            Record(track: "Short", artist: "Y", ms: 10_000),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal("Long", r.Tracks[0].Track);            // by time
        Assert.Equal("Short", r.TracksByPlayCount[0].Track); // by plays
        Assert.Equal(3, r.TracksByPlayCount[0].PlayCount);
    }

    [Fact]
    public void RecordsWithoutTimestamp_HaveNullFirstAndLastPlayed()
    {
        var records = new[] { Record(ts: DateTime.MinValue) };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Single(r.Tracks);
        Assert.Null(r.Tracks[0].FirstPlayed);
        Assert.Null(r.Tracks[0].LastPlayed);
        Assert.Null(r.FirstListen);
        Assert.Null(r.LastListen);
    }

    [Fact]
    public void Streaks_LongestAndCurrent()
    {
        var records = new[]
        {
            Record(ts: new DateTime(2023, 5, 1, 10, 0, 0)),
            Record(ts: new DateTime(2023, 5, 2, 10, 0, 0)),
            Record(ts: new DateTime(2023, 5, 3, 10, 0, 0)),
            Record(ts: new DateTime(2023, 5, 5, 10, 0, 0)),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal(3, r.LongestStreakDays);
        Assert.Equal(new DateTime(2023, 5, 1), r.LongestStreakStart);
        Assert.Equal(new DateTime(2023, 5, 3), r.LongestStreakEnd);
        Assert.Equal(1, r.CurrentStreakDays);
        Assert.Equal(4, r.ActiveDays);
    }

    [Fact]
    public void BiggestDay_PicksDayWithMostListening()
    {
        var records = new[]
        {
            Record(ms: 10_000, ts: new DateTime(2023, 5, 1, 10, 0, 0)),
            Record(ms: 100_000, ts: new DateTime(2023, 5, 2, 10, 0, 0)),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal(new DateTime(2023, 5, 2), r.BiggestDay);
        Assert.Equal(100_000, r.BiggestDayMs);
    }

    [Fact]
    public void Sessions_SplitOnGapsAndTrackLongest()
    {
        var records = new[]
        {
            // Session 1: two plays ending 10:00 and 10:20 (second starts 10:19).
            Record(ms: 60_000, ts: new DateTime(2023, 5, 1, 10, 0, 0)),
            Record(ms: 60_000, ts: new DateTime(2023, 5, 1, 10, 20, 0)),
            // Session 2: starts 11:59, more than 30 min after 10:20.
            Record(ms: 60_000, ts: new DateTime(2023, 5, 1, 12, 0, 0)),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal(2, r.SessionCount);
        Assert.Equal(120_000, r.LongestSessionMs);
        Assert.Equal(90_000, r.AvgSessionMs);
        Assert.Equal(new DateTime(2023, 5, 1), r.LongestSessionDate);
    }

    [Fact]
    public void Years_RollUpWithTopArtistAndTrack()
    {
        var records = new[]
        {
            Record(track: "Song A", artist: "X", ms: 100_000, ts: new DateTime(2022, 3, 1, 10, 0, 0)),
            Record(track: "Song B", artist: "Y", ms: 40_000, ts: new DateTime(2022, 6, 1, 10, 0, 0)),
            Record(track: "Song C", artist: "Z", ms: 70_000, ts: new DateTime(2023, 1, 1, 10, 0, 0)),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal(2, r.Years.Count);
        var y2022 = r.Years[0];
        Assert.Equal(2022, y2022.Year);
        Assert.Equal(140_000, y2022.TotalMsPlayed);
        Assert.Equal(2, y2022.PlayCount);
        Assert.Equal(2, y2022.UniqueArtists);
        Assert.Equal(2, y2022.UniqueTracks);
        Assert.Equal("X", y2022.TopArtist);
        Assert.Equal("Song A", y2022.TopTrack);
        Assert.Equal(2023, r.Years[1].Year);
    }

    [Fact]
    public void SkipStats_IncludeShortPlaysDroppedFromTotals()
    {
        var filter = new FilterOptions { MinMsPlayed = 5_000 };
        var records = new[]
        {
            Record(track: "S", ms: 1_000, skipped: true),
            Record(track: "S", ms: 60_000),
        };

        var r = AnalysisEngine.Analyze(records, filter);

        Assert.Equal(1, r.TotalPlays);          // short play excluded from totals
        Assert.Equal(2, r.SkipEligiblePlays);   // but counted for skip stats
        Assert.Equal(1, r.TotalSkips);
        var skipStat = Assert.Single(r.SkippedTracks);
        Assert.Equal(50, skipStat.SkipRate);
    }

    [Fact]
    public void ReasonEnds_CountAndGroupMissingAsUnknown()
    {
        var records = new[]
        {
            Record(reasonEnd: "trackdone"),
            Record(reasonEnd: "trackdone"),
            Record(reasonEnd: "fwdbtn"),
            Record(reasonEnd: null),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal(3, r.ReasonEnds.Count);
        Assert.Equal("trackdone", r.ReasonEnds[0].Reason);
        Assert.Equal(2, r.ReasonEnds[0].Count);
        Assert.Contains(r.ReasonEnds, x => x.Reason == "unknown" && x.Count == 1);
    }

    [Fact]
    public void NewArtistsByMonth_CountsFirstListens()
    {
        var records = new[]
        {
            Record(artist: "X", ts: new DateTime(2023, 1, 5, 10, 0, 0)),
            Record(artist: "X", ts: new DateTime(2023, 2, 5, 10, 0, 0)), // repeat, not new
            Record(artist: "Y", ts: new DateTime(2023, 1, 20, 10, 0, 0)),
            Record(artist: "Z", ts: new DateTime(2023, 3, 5, 10, 0, 0)),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal(2, r.NewArtistsByMonth.Count);
        Assert.Equal(new DateTime(2023, 1, 1), r.NewArtistsByMonth[0].Date);
        Assert.Equal(2, r.NewArtistsByMonth[0].Value);
        Assert.Equal(new DateTime(2023, 3, 1), r.NewArtistsByMonth[1].Date);
        Assert.Equal(1, r.NewArtistsByMonth[1].Value);
    }

    [Fact]
    public void EmptyInput_ProducesEmptyResult()
    {
        var r = AnalysisEngine.Analyze(Array.Empty<PlayRecord>(), NoFilter);

        Assert.Equal(0, r.TotalPlays);
        Assert.Equal(0, r.SessionCount);
        Assert.Equal(0, r.LongestStreakDays);
        Assert.Equal(0, r.CurrentStreakDays);
        Assert.Null(r.BiggestDay);
        Assert.Empty(r.Years);
    }

    [Fact]
    public void FirstTrack_TracksArtistsEarliestListen()
    {
        var records = new[]
        {
            Record(track: "Later", artist: "X", ts: new DateTime(2023, 5, 2, 10, 0, 0)),
            Record(track: "Earlier", artist: "X", ts: new DateTime(2023, 5, 1, 10, 0, 0)),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal("Earlier", r.Artists[0].FirstTrack);
        Assert.Equal(new DateTime(2023, 5, 1, 10, 0, 0), r.Artists[0].FirstPlayed);
        Assert.Equal(new DateTime(2023, 5, 2, 10, 0, 0), r.Artists[0].LastPlayed);
    }

    // ---- Podcasts and audiobooks ---------------------------------------------------------

    private static PlayRecord Podcast(
        string episode = "Ep 1", string show = "Show", int ms = 900_000,
        DateTime? ts = null, ContentKind kind = ContentKind.Podcast)
        => new()
        {
            Kind = kind,
            ShowName = show,
            EpisodeName = episode,
            MsPlayed = ms,
            Timestamp = ts ?? new DateTime(2023, 5, 10, 14, 0, 0),
        };

    [Fact]
    public void Podcasts_AreExcludedFromMusicStats_ByDefault()
    {
        var records = new[]
        {
            Record(track: "A", artist: "X", ms: 100_000),
            Podcast(episode: "Ep 1", show: "Show", ms: 900_000),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal(1, r.TotalPlays);
        Assert.Equal(100_000, r.TotalMsPlayed);
        Assert.Equal(1, r.UniqueTracks);
        Assert.DoesNotContain(r.Artists, a => a.Artist == "Show");
    }

    [Fact]
    public void Podcasts_AreAggregatedSeparately_RegardlessOfToggle()
    {
        var records = new[]
        {
            Record(track: "A", artist: "X", ms: 100_000),
            Podcast(episode: "Ep 1", show: "Show", ms: 900_000),
            Podcast(episode: "Ep 2", show: "Show", ms: 600_000),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        var show = Assert.Single(r.Shows);
        Assert.Equal("Show", show.Show);
        Assert.Equal(1_500_000, show.TotalMsPlayed);
        Assert.Equal(2, show.PlayCount);
        Assert.Equal(2, show.EpisodeCount);
        Assert.Equal(2, r.Episodes.Count);
        Assert.Equal(1_500_000, r.PodcastMsPlayed);
        Assert.Equal(2, r.PodcastPlays);
    }

    [Fact]
    public void EpisodeCount_CountsDistinctEpisodes_NotPlays()
    {
        var records = new[]
        {
            Podcast(episode: "Ep 1", show: "Show"),
            Podcast(episode: "Ep 1", show: "Show"),
            Podcast(episode: "Ep 1", show: "Show"),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        var show = Assert.Single(r.Shows);
        Assert.Equal(3, show.PlayCount);
        Assert.Equal(1, show.EpisodeCount);
    }

    [Fact]
    public void IncludePodcasts_FoldsShowsIntoMusicStats_AsArtistAndTrack()
    {
        var records = new[]
        {
            Record(track: "A", artist: "X", ms: 100_000),
            Podcast(episode: "Ep 1", show: "Show", ms: 900_000),
        };

        var r = AnalysisEngine.Analyze(records, new FilterOptions { MinMsPlayed = 0, IncludePodcasts = true });

        Assert.Equal(2, r.TotalPlays);
        Assert.Equal(1_000_000, r.TotalMsPlayed);
        Assert.Equal("Show", r.Artists[0].Artist);   // the longer listen ranks first
        Assert.Contains(r.Tracks, t => t.Track == "Ep 1" && t.Artist == "Show");
    }

    [Fact]
    public void Audiobooks_AreReportedAsShows_WithTheirOwnKind()
    {
        var records = new[]
        {
            Podcast(episode: "Chapter 1", show: "A Long Book", kind: ContentKind.Audiobook),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        var show = Assert.Single(r.Shows);
        Assert.Equal(ContentKind.Audiobook, show.Kind);
        Assert.Equal("A Long Book", show.Show);
    }

    // ---- Playback context -------------------------------------------------------------------

    private static PlayRecord Context(
        string platform = "", string country = "", bool shuffle = false,
        bool offline = false, bool incognito = false, bool hasFlags = true, int ms = 60_000)
        => new()
        {
            TrackName = "T",
            ArtistName = "A",
            AlbumName = "Al",
            MsPlayed = ms,
            Timestamp = new DateTime(2023, 5, 10, 14, 0, 0),
            Platform = platform,
            Country = country,
            Shuffle = shuffle,
            Offline = offline,
            Incognito = incognito,
            HasPlaybackFlags = hasFlags,
        };

    [Theory]
    [InlineData("windows 10 (10.0.19043; x64; AppX)", "Desktop")]
    [InlineData("OS X 13.0 [x86_64]", "Desktop")]
    [InlineData("android-tablet", "Mobile")]
    [InlineData("iOS 16.1 (iPhone14,5)", "Mobile")]
    [InlineData("web_player linux undefined;chrome", "Web player")]
    [InlineData("Partner sonos_bose", "Speaker / cast")]
    [InlineData("something unknown", "Other")]
    public void PlatformFamily_BucketsSpotifyPlatformStrings(string raw, string expected)
    {
        Assert.Equal(expected, AnalysisEngine.PlatformFamily(raw));
    }

    [Fact]
    public void Platforms_AreGroupedByFamily()
    {
        var records = new[]
        {
            Context(platform: "windows 10 (10.0.19043; x64; AppX)", ms: 100_000),
            Context(platform: "OS X 13.0", ms: 50_000),
            Context(platform: "android", ms: 30_000),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal(2, r.Platforms.Count);
        Assert.Equal("Desktop", r.Platforms[0].Name);
        Assert.Equal(150_000, r.Platforms[0].TotalMsPlayed);
        Assert.Equal(2, r.Platforms[0].PlayCount);
        Assert.Equal("Mobile", r.Platforms[1].Name);
    }

    [Fact]
    public void Countries_AreAggregated()
    {
        var records = new[]
        {
            Context(country: "CA", ms: 100_000),
            Context(country: "CA", ms: 50_000),
            Context(country: "US", ms: 30_000),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal(2, r.Countries.Count);
        Assert.Equal("CA", r.Countries[0].Name);
        Assert.Equal(150_000, r.Countries[0].TotalMsPlayed);
    }

    [Fact]
    public void ShuffleAndOffline_CountOnlyRowsCarryingTheFlags()
    {
        var records = new[]
        {
            Context(shuffle: true, offline: true),
            Context(shuffle: false),
            // Legacy rows carry no context at all and must not dilute the denominator.
            Context(hasFlags: false),
            Context(hasFlags: false),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        Assert.Equal(2, r.ShuffleEligiblePlays);
        Assert.Equal(1, r.ShufflePlays);
        Assert.Equal(1, r.OfflinePlays);
        Assert.Equal(0, r.IncognitoPlays);
    }

    [Fact]
    public void SkippedTracks_OnlyIncludeTracksThatWereActuallySkipped()
    {
        var records = new[]
        {
            Record(track: "Skipped", artist: "X", skipped: true),
            Record(track: "Skipped", artist: "X", skipped: false),
            Record(track: "Never", artist: "X", skipped: false),
        };

        var r = AnalysisEngine.Analyze(records, NoFilter);

        var skipped = Assert.Single(r.SkippedTracks);
        Assert.Equal("Skipped", skipped.Track);
        Assert.Equal("X", skipped.Artist);
        Assert.Equal(1, skipped.SkipCount);
        Assert.Equal(2, skipped.PlayCount);   // denominator keeps the unskipped play
        Assert.Equal(50.0, skipped.SkipRate);
    }
}
