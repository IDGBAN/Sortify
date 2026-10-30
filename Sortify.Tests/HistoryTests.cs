using System.IO;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>The History tab: every play, and the CSV of them.</summary>
public class HistoryTests
{
    private static PlayRecord Play(string track, DateTime ts, string artist = "Artist", int ms = 200_000,
        string? reason = "trackdone", string platform = "windows 10 (10.0.22631; x64)") => new()
        {
            TrackName = track,
            ArtistName = artist,
            AlbumName = "Album",
            MsPlayed = ms,
            Timestamp = ts,
            ReasonEnd = reason,
            Platform = platform,
            Country = "CA",
            HasPlaybackFlags = true,
        };

    private static readonly DateTime Day = new(2024, 3, 14);

    private static List<PlayRecord> ThreeDays() => new()
    {
        Play("Monday Song", Day.AddDays(-3).AddHours(9)),
        Play("Thursday Morning", Day.AddHours(8)),
        Play("Thursday Night", Day.AddHours(22)),
        Play("Friday Song", Day.AddDays(1).AddHours(12), artist: "Other"),
    };

    private static HistoryViewModel ViewModelFor(List<PlayRecord> records, FilterOptions? filter = null) =>
        new(() => new HistoryViewModel.Source(records, filter ?? new FilterOptions()));

    [Fact]
    public void Build_ListsThePlaysTheFiltersLetThrough_OldestFirst()
    {
        var records = ThreeDays();
        records.Reverse();
        var filter = new FilterOptions();
        filter.ExcludedArtists.Add("Other");

        var rows = HistoryViewModel.Build(records, filter);

        Assert.Equal(new[] { "Monday Song", "Thursday Morning", "Thursday Night" }, rows.Select(r => r.Track));
        Assert.Equal(new[] { 0, 1, 2 }, rows.Select(r => r.Order));
    }

    [Fact]
    public void Build_LeavesOutPlaysUnderTheMinimumDuration_ButKeepsPodcasts()
    {
        var records = new List<PlayRecord>
        {
            Play("Skipped", Day, ms: 2_000),
            new() { Kind = ContentKind.Podcast, ShowName = "Show", EpisodeName = "Ep", MsPlayed = 900_000, Timestamp = Day.AddHours(1) },
        };

        var rows = HistoryViewModel.Build(records, new FilterOptions { IncludePodcasts = false });

        var row = Assert.Single(rows);
        Assert.Equal("Ep", row.Track);
        Assert.Equal("Show", row.Artist);
        Assert.Equal("Podcast", row.Album);
    }

    [Fact]
    public void Rows_DescribeEachPlayInWords()
    {
        var plain = new PlayRow(Play("A", Day.AddHours(13.5), reason: "fwdbtn", platform: "android OS 14 API 34 (Google, Pixel 8)"), "Mobile", 0);
        Assert.Equal("2024-03-14 13:30", plain.When);
        Assert.Equal("Skipped (next)", plain.Ended);
        Assert.Equal("Mobile", plain.Device);
        Assert.Equal(string.Empty, plain.Mode);

        var flagged = new PlayRow(new PlayRecord { Shuffle = true, Incognito = true, Timestamp = DateTime.MinValue }, string.Empty, 0);
        Assert.Equal("Shuffle, Private", flagged.Mode);
        Assert.Equal(string.Empty, flagged.When);
        Assert.Equal(string.Empty, flagged.Ended);

        var book = new PlayRow(new PlayRecord { Kind = ContentKind.Audiobook, ShowName = "Book", EpisodeName = "Chapter 1" }, string.Empty, 0);
        Assert.Equal("Audiobook", book.Album);
    }

    [Theory]
    [InlineData(true, "Friday Song")]
    [InlineData(false, "Monday Song")]
    public void SortingByTime_PutsTheNewestOrOldestFirst(bool descending, string first)
    {
        var rows = HistoryViewModel.Build(ThreeDays(), new FilterOptions());

        var sorted = HistoryViewModel.Sorted(rows, nameof(PlayRow.When), descending);

        Assert.Equal(first, sorted[0].Track);
        Assert.Equal(4, sorted.Count);
    }

    [Fact]
    public void SortingByName_IgnoresCase_AndBreaksTiesNewestFirst()
    {
        var records = new List<PlayRecord>
        {
            Play("beta", Day.AddHours(1)),
            Play("Alpha", Day.AddHours(2)),
            Play("alpha", Day.AddHours(3)),
        };
        var rows = HistoryViewModel.Build(records, new FilterOptions());

        var sorted = HistoryViewModel.Sorted(rows, nameof(PlayRow.Track), descending: false);

        Assert.Equal(new[] { "alpha", "Alpha", "beta" }, sorted.Select(r => r.Track));
        Assert.Equal(sorted.Select(r => r.Order), HistoryViewModel.Sorted(sorted, nameof(PlayRow.Track), false).Select(r => r.Order));
    }

    [Fact]
    public void SortingByLength_PutsTheLongestFirstWhenDescending()
    {
        var records = new List<PlayRecord>
        {
            Play("Short", Day.AddHours(1), ms: 60_000),
            Play("Long", Day.AddHours(2), ms: 600_000),
            Play("Middle", Day.AddHours(3), ms: 200_000),
        };

        var sorted = HistoryViewModel.Sorted(HistoryViewModel.Build(records, new FilterOptions()), nameof(PlayRow.Played), true);

        Assert.Equal(new[] { "Long", "Middle", "Short" }, sorted.Select(r => r.Track));
    }

    [Fact]
    public async Task TheTab_ListsNewestFirst_AndTheRowFilterNarrowsIt()
    {
        var vm = ViewModelFor(ThreeDays());
        vm.IsVisible = true;
        await vm.UpdateAsync();

        Assert.Equal("Friday Song", vm.Rows[0].Track);
        Assert.Equal("4 plays", vm.Summary);

        vm.QuickFilter = "thursday";

        Assert.Equal(new[] { "Thursday Night", "Thursday Morning" }, vm.Rows.Select(r => r.Track));
        Assert.Equal("2 of 4 plays", vm.Summary);

        vm.QuickFilter = "nothing like it";
        Assert.Empty(vm.Rows);
        Assert.Contains("nothing like it", vm.EmptyText);
    }

    [Fact]
    public async Task NoPlaysUnderTheFilters_SaysSo()
    {
        var filter = new FilterOptions { SearchTerm = "nobody" };
        var vm = ViewModelFor(ThreeDays(), filter);

        await vm.UpdateAsync();

        Assert.Empty(vm.Rows);
        Assert.Equal("No plays match the current filters.", vm.EmptyText);
    }

    [Fact]
    public async Task WhileHidden_TheRowsWaitUntilTheTabIsShown()
    {
        int built = 0;
        var records = ThreeDays();
        var vm = new HistoryViewModel(() =>
        {
            built++;
            return new HistoryViewModel.Source(records, new FilterOptions());
        });

        vm.Invalidate();
        Assert.Equal(0, built);

        vm.IsVisible = true;
        await vm.UpdateAsync();
        Assert.True(built >= 1);
        Assert.Equal(4, vm.Rows.Count);
    }

    [Fact]
    public async Task Sorting_KeepsTheRowFilter()
    {
        var vm = ViewModelFor(ThreeDays());
        await vm.UpdateAsync();
        vm.QuickFilter = "song";

        await vm.SortAsync(nameof(PlayRow.Track), descending: false);

        Assert.Equal(new[] { "Friday Song", "Monday Song" }, vm.Rows.Select(r => r.Track));
    }

    [Fact]
    public async Task GoingToADay_LandsOnItsLastPlay()
    {
        var vm = ViewModelFor(ThreeDays());
        await vm.UpdateAsync();
        PlayRow? landed = null;
        vm.ScrollRequested += (_, row) => landed = row;

        vm.GoToDate = Day;
        await vm.GoToAsync(Day);

        Assert.Equal("Thursday Night", landed?.Track);
        Assert.Equal(string.Empty, vm.JumpNote);
    }

    [Fact]
    public async Task GoingToADayWithNoPlays_LandsOnTheOneBefore_AndSaysSo()
    {
        var vm = ViewModelFor(ThreeDays());
        await vm.UpdateAsync();
        PlayRow? landed = null;
        vm.ScrollRequested += (_, row) => landed = row;

        vm.GoToDate = Day.AddDays(-1);
        await vm.GoToAsync(Day.AddDays(-1));

        Assert.Equal("Monday Song", landed?.Track);
        Assert.Contains("2024-03-13", vm.JumpNote);
    }

    [Fact]
    public async Task GoingToADayBeforeTheHistory_LandsOnTheFirstPlay()
    {
        var vm = ViewModelFor(ThreeDays());
        await vm.UpdateAsync();
        PlayRow? landed = null;
        vm.ScrollRequested += (_, row) => landed = row;

        vm.GoToDate = new DateTime(2020, 1, 1);
        await vm.GoToAsync(new DateTime(2020, 1, 1));

        Assert.Equal("Monday Song", landed?.Track);
        Assert.Contains("first play", vm.JumpNote);
    }

    [Fact]
    public async Task GoingToTheLastDayOfTheCalendar_LandsOnTheNewestPlay()
    {
        var vm = ViewModelFor(ThreeDays());
        await vm.UpdateAsync();
        PlayRow? landed = null;
        vm.ScrollRequested += (_, row) => landed = row;

        vm.GoToDate = DateTime.MaxValue.Date;
        await vm.GoToAsync(DateTime.MaxValue.Date);

        Assert.Equal("Friday Song", landed?.Track);
        Assert.Contains("9999-12-31", vm.JumpNote);
    }

    [Fact]
    public async Task GoingToADay_PutsTheRowsBackInTimeOrder()
    {
        var vm = ViewModelFor(ThreeDays());
        await vm.UpdateAsync();
        await vm.SortAsync(nameof(PlayRow.Track), descending: false);
        bool reset = false;
        vm.SortReset += (_, _) => reset = true;

        vm.GoToDate = Day;
        await vm.GoToAsync(Day);

        Assert.True(reset);
        Assert.Equal(nameof(PlayRow.When), vm.SortColumn);
        Assert.True(vm.SortDescending);
        Assert.Equal("Friday Song", vm.Rows[0].Track);
    }

    [Fact]
    public void APlay_OpensItsTrack_OrItsEpisode()
    {
        var music = new PlayRow(Play("Song", Day), "Desktop", 0);
        var episode = new PlayRow(new PlayRecord { Kind = ContentKind.Podcast, ShowName = "Show", EpisodeName = "Ep" }, string.Empty, 0);

        Assert.Equal((DetailScope.Track, "Song", "Artist"), DetailWindow.TargetFor(music));
        Assert.Equal((DetailScope.Episode, "Ep", "Show"), DetailWindow.TargetFor(episode));
    }

    [Fact]
    public async Task TheCsv_HasOneRowPerPlay()
    {
        var records = new List<PlayRecord>
        {
            Play("Comma, Song", Day.AddHours(9).AddSeconds(7), reason: "fwdbtn"),
            new()
            {
                Kind = ContentKind.Podcast, ShowName = "Show", EpisodeName = "Ep 1", MsPlayed = 900_000,
                Timestamp = Day.AddHours(10), ReasonEnd = "endplay",
            },
        };
        var path = Path.Combine(Path.GetTempPath(), "SortifyPlays_" + Guid.NewGuid().ToString("N") + ".csv");

        try
        {
            await ExportService.SavePlaysCsvAsync(path, records);
            var lines = await File.ReadAllLinesAsync(path);

            Assert.Equal(3, lines.Length);
            Assert.StartsWith("Ended,Kind,Track,Artist,Album,Show,Episode,MsPlayed,ReasonEnd", lines[0]);
            Assert.Equal("2024-03-14 09:00:07,Music,\"Comma, Song\",Artist,Album,,,200000,fwdbtn,false,Desktop,windows 10 (10.0.22631; x64),CA,false,false,false,", lines[1]);
            // A podcast names its show and episode, and an export that didn't record the
            // playback flags leaves them blank.
            Assert.Equal("2024-03-14 10:00:00,Podcast,,,,Show,Ep 1,900000,endplay,false,,,,,,,", lines[2]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
