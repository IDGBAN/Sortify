using System.Windows.Controls;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>How a summary card names the days it covers.</summary>
public class CardRangeTests
{
    private static CardRange Range(int y1, int m1, int d1, int y2, int m2, int d2) =>
        new(new DateTime(y1, m1, d1), new DateTime(y2, m2, d2));

    [Fact]
    public void AYear_KeepsItsYearInReviewWording()
    {
        var year = CardRange.ForYear(2024);

        Assert.Equal(2024, year.Year);
        Assert.Equal(("MY YEAR IN MUSIC", "2024", "in 2024"), (year.Kicker, year.Title, year.Phrase));
        Assert.Equal("Sortify_2024_in_review", year.FileStem);
    }

    [Fact]
    public void AMonth_IsNamedByMonth()
    {
        var march = Range(2024, 3, 1, 2024, 3, 31);

        Assert.Null(march.Year);
        Assert.Equal(("MY MONTH IN MUSIC", "March 2024", "in March 2024"), (march.Kicker, march.Title, march.Phrase));
        Assert.Equal("Sortify_summary_2024-03", march.FileStem);
    }

    [Fact]
    public void WholeMonthsAndYears_AreCountedAsSuch()
    {
        var summer = Range(2024, 6, 1, 2024, 8, 31);
        Assert.Equal(("MY 3 MONTHS IN MUSIC", "June to August 2024"), (summer.Kicker, summer.Title));
        Assert.Equal("between June and August 2024", summer.Phrase);

        var winter = Range(2023, 11, 1, 2024, 2, 29);
        Assert.Equal(("MY 4 MONTHS IN MUSIC", "November 2023 to February 2024"), (winter.Kicker, winter.Title));

        var years = Range(2019, 1, 1, 2024, 12, 31);
        Assert.Equal(("MY 6 YEARS IN MUSIC", "2019 to 2024", "between 2019 and 2024"), (years.Kicker, years.Title, years.Phrase));
        Assert.Null(years.Year);
    }

    [Fact]
    public void TheLastTwelveMonths_ReadAsAYear()
    {
        var last12 = Range(2025, 10, 4, 2026, 10, 3);

        Assert.Equal(("MY YEAR IN MUSIC", "Oct 4, 2025 to Oct 3, 2026"), (last12.Kicker, last12.Title));
        Assert.Null(last12.Year);
    }

    [Fact]
    public void TheLastThirtyDays_ReadAsThirtyDays_WhicheverMonthTheyEndIn()
    {
        Assert.Equal("MY 30 DAYS IN MUSIC", Range(2026, 9, 4, 2026, 10, 3).Kicker);
        Assert.Equal("MY 30 DAYS IN MUSIC", Range(2026, 10, 5, 2026, 11, 3).Kicker);
        Assert.Equal("Sep 4 to Oct 3, 2026", Range(2026, 9, 4, 2026, 10, 3).Title);
    }

    [Fact]
    public void DaysAndWeeks_AreNamedByTheirDates()
    {
        var day = Range(2024, 3, 14, 2024, 3, 14);
        Assert.Equal(("MY DAY IN MUSIC", "March 14, 2024", "on March 14, 2024"), (day.Kicker, day.Title, day.Phrase));
        Assert.Equal("Sortify_summary_2024-03-14", day.FileStem);

        var week = Range(2024, 3, 4, 2024, 3, 10);
        Assert.Equal(("MY WEEK IN MUSIC", "Mar 4 to 10, 2024"), (week.Kicker, week.Title));

        var acrossYears = Range(2023, 12, 20, 2024, 1, 5);
        Assert.Equal(("MY 17 DAYS IN MUSIC", "Dec 20, 2023 to Jan 5, 2024"), (acrossYears.Kicker, acrossYears.Title));
        Assert.Equal("between Dec 20, 2023 and Jan 5, 2024", acrossYears.Phrase);
        Assert.Equal("Sortify_summary_2023-12-20_to_2024-01-05", acrossYears.FileStem);

        Assert.Equal("MY 1,188 DAYS IN MUSIC", Range(2021, 3, 2, 2024, 6, 1).Kicker);
    }

    [Fact]
    public void NoDates_CoverTheWholeHistory()
    {
        var all = CardRange.For(null, null, new DateTime(2019, 3, 2, 8, 0, 0), new DateTime(2024, 6, 1, 23, 0, 0))!;

        Assert.True(all.AllTime);
        Assert.Null(all.Year);
        Assert.Equal(("MY MUSIC, ALL TIME", "2019 to 2024", "in your history"), (all.Kicker, all.Title, all.Phrase));
        Assert.Equal("Sortify_summary_all_time", all.FileStem);
        Assert.Equal("2024", CardRange.For(null, null, new DateTime(2024, 1, 5), new DateTime(2024, 6, 1))!.Title);
    }

    [Fact]
    public void AnOpenEnd_StopsAtTheFirstOrLastPlay()
    {
        var first = new DateTime(2019, 3, 2, 8, 0, 0);
        var last = new DateTime(2024, 6, 1, 23, 0, 0);

        var since = CardRange.For(new DateTime(2024, 1, 1), null, first, last)!;
        Assert.Equal((new DateTime(2024, 1, 1), last.Date, false), (since.From, since.To, since.AllTime));

        var until = CardRange.For(null, new DateTime(2020, 12, 31), first, last)!;
        Assert.Equal((first.Date, new DateTime(2020, 12, 31)), (until.From, until.To));

        Assert.Null(CardRange.For(null, null, null, null));
    }

    [Fact]
    public void TheWordsStayEnglish_OnAnyCalendar()
    {
        using var _ = CultureScope.Named("ar-SA");

        Assert.Equal("March 2024", Range(2024, 3, 1, 2024, 3, 31).Title);
        Assert.Equal("MY 1,188 DAYS IN MUSIC", Range(2021, 3, 2, 2024, 6, 1).Kicker);
    }
}

/// <summary>A summary card for any range of dates.</summary>
public class SummaryCardBuilderTests
{
    private static PlayRecord Play(string track, string artist, int minutes, DateTime when) => new()
    {
        TrackName = track,
        ArtistName = artist,
        AlbumName = artist + " LP",
        MsPlayed = minutes * 60_000,
        Timestamp = when,
    };

    // Weeks starting on Sunday and on Monday split March's plays differently.
    private static readonly List<PlayRecord> Records = new()
    {
        Play("Old", "Old Friend", 60, new DateTime(2023, 5, 1, 12, 0, 0)),
        Play("Old", "Old Friend", 30, new DateTime(2024, 3, 1, 12, 0, 0)),
        Play("Hit", "New Find", 90, new DateTime(2024, 3, 5, 12, 0, 0)),
        Play("Hit", "New Find", 90, new DateTime(2024, 3, 6, 12, 0, 0)),
        Play("Hit", "New Find", 30, new DateTime(2024, 3, 10, 12, 0, 0)),
        Play("Deep Cut", "Another", 20, new DateTime(2024, 3, 20, 12, 0, 0)),
        Play("Later", "Later", 60, new DateTime(2024, 9, 1, 12, 0, 0)),
    };

    private static YearReview Build(DateTime? from, DateTime? to, DayOfWeek firstDay = DayOfWeek.Sunday, FilterOptions? filter = null) =>
        YearReviewBuilder.BuildForDates(Records, filter ?? new FilterOptions(), from, to, string.Empty, firstDayOfWeek: firstDay);

    [Fact]
    public void AMonth_NamesItsBiggestWeek_NotItsBiggestMonth()
    {
        var march = Build(new DateTime(2024, 3, 1), new DateTime(2024, 3, 31));

        Assert.Equal("March 2024", march.Range.Title);
        Assert.Equal(5, march.Plays);
        Assert.Equal(string.Empty, march.BiggestMonthText);
        Assert.Equal("March 3 to 9, 3 hours", march.BiggestWeekText);
        Assert.Equal("March 4 to 10, 3.5 hours", Build(new DateTime(2024, 3, 1), new DateTime(2024, 3, 31), DayOfWeek.Monday).BiggestWeekText);
        Assert.Equal("March 5, 1h 30m", march.BiggestDayText);
    }

    [Fact]
    public void ABiggestWeek_IsCutToTheRange()
    {
        // Weeks starting Sunday put March 1 in the week of February 25.
        var review = Build(new DateTime(2024, 3, 1), new DateTime(2024, 3, 16), filter: new FilterOptions { ExcludedArtists = { "New Find" } });

        Assert.Equal("March 1 to 2, 0.5 hours", review.BiggestWeekText);
    }

    [Fact]
    public void AFewDays_NameNeitherAMonthNorAWeek()
    {
        var review = Build(new DateTime(2024, 3, 4), new DateTime(2024, 3, 10));

        Assert.Equal("MY WEEK IN MUSIC", review.Range.Kicker);
        Assert.Equal((string.Empty, string.Empty), (review.BiggestMonthText, review.BiggestWeekText));
        Assert.Equal("March 5, 1h 30m", review.BiggestDayText);
    }

    [Fact]
    public void ARangeAcrossYears_PutsTheYearOnItsDates()
    {
        var review = Build(new DateTime(2023, 5, 1), new DateTime(2024, 9, 30));

        Assert.Equal("March 2024, 4.3 hours", review.BiggestMonthText);
        Assert.Equal("March 5, 2024, 1h 30m", review.BiggestDayText);
    }

    [Fact]
    public void NewArtists_AreThoseFirstHeardWithinTheRange()
    {
        Assert.Equal(2, Build(new DateTime(2024, 3, 1), new DateTime(2024, 3, 31)).NewArtists);
        Assert.Equal(0, Build(new DateTime(2024, 3, 1), new DateTime(2024, 3, 1)).NewArtists);
    }

    [Fact]
    public void TheFigures_SuitTheRange()
    {
        static IEnumerable<string> Labels(YearReview r) => r.Stats.Select(s => s.Label);

        Assert.Equal(new[] { "plays", "artists", "new artists", "days in a row" }, Labels(Build(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31))));
        Assert.Equal(new[] { "plays", "artists", "days in a row", "tracks" }, Labels(Build(null, null)));
        Assert.Equal(new[] { "play", "artist", "new artist", "track" }, Labels(Build(new DateTime(2024, 3, 20), new DateTime(2024, 3, 20))));
    }

    [Fact]
    public void NoDates_MakeAnAllTimeCard()
    {
        var review = Build(null, null);

        Assert.True(review.Range.AllTime);
        Assert.Equal("2023 to 2024", review.Range.Title);
        Assert.Equal(Records.Count, review.Plays);
        Assert.DoesNotContain(review.Stats, s => s.Label.Contains("new"));
    }

    [Fact]
    public void AnOpenEnd_RunsToTheLastPlay()
    {
        var review = Build(new DateTime(2024, 3, 6), null);

        Assert.Equal((new DateTime(2024, 3, 6), new DateTime(2024, 9, 1)), (review.Range.From, review.Range.To));
        Assert.Equal(4, review.Plays);
    }

    [Fact]
    public void AYearsCard_IsTheSameAsItsDates()
    {
        var year = YearReviewBuilder.Build(Records, new FilterOptions(), 2024, string.Empty);
        var dates = Build(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));

        Assert.Equal(2024, year.Year);
        Assert.Equal((year.Plays, year.NewArtists, year.BiggestMonthText, year.BiggestDayText),
            (dates.Plays, dates.NewArtists, dates.BiggestMonthText, dates.BiggestDayText));
    }

    [Fact]
    public void SmallNumbers_ReadAsSingular()
    {
        var day = Build(new DateTime(2024, 3, 20), new DateTime(2024, 3, 20));

        Assert.Equal("minutes", day.MinutesWord);
        Assert.Equal(string.Empty, day.HoursText);
        Assert.Equal("minute", new YearReview { Range = day.Range, TotalMsPlayed = 60_000 }.MinutesWord);
        Assert.Equal("1 hour of listening", new YearReview { Range = day.Range, TotalMsPlayed = 3_600_000 }.HoursText);
        Assert.Equal("2.5 hours of listening", new YearReview { Range = day.Range, TotalMsPlayed = 9_000_000 }.HoursText);
        Assert.Equal("1,200 hours of listening", new YearReview { Range = day.Range, TotalMsPlayed = 4_320_000_000 }.HoursText);
    }
}

/// <summary>The Overview's card button and the window it opens.</summary>
[Collection(WpfCollection.Name)]
public class SummaryCardViewTests
{
    [Fact]
    public async Task TheButton_NamesTheRangeInTheSidebar()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(new AppSettings());
            vm.Filters.ResetCommand.Execute(null);
            Assert.False(vm.CanMakeSummaryCard);

            await vm.OpenSampleCommand.ExecuteAsync(null);
            Assert.True(vm.CanMakeSummaryCard);
            Assert.Equal("Summary card for all time", vm.SummaryCardText);

            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            int year = DateTime.Today.Year - 1;
            vm.Filters.SetRange(new DateTime(year, 3, 1), new DateTime(year, 3, 31));
            await Wait.UntilAsync(() => vm.SummaryCardText != "Summary card for all time");

            Assert.Equal($"Summary card for March {year}", vm.SummaryCardText);
            Assert.Contains(nameof(MainViewModel.CanMakeSummaryCard), raised);
        });
    }

    [Fact]
    public async Task TheCard_CoversTheSidebarDates_UnderTheOtherFilters()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(new AppSettings());
            vm.Filters.ResetCommand.Execute(null);
            await vm.OpenSampleCommand.ExecuteAsync(null);

            var all = (await vm.BuildSummaryCardAsync())!;
            Assert.True(all.Range.AllTime);
            string top = all.TopArtists[0].Name;

            int year = DateTime.Today.Year - 1;
            vm.Filters.SetRange(new DateTime(year, 6, 1), new DateTime(year, 8, 31));
            vm.Filters.ExcludedArtists.Add(top);
            var summer = (await vm.BuildSummaryCardAsync())!;

            Assert.Equal($"June to August {year}", summer.Range.Title);
            Assert.True(summer.Plays > 0);
            Assert.DoesNotContain(summer.TopArtists, a => a.Name == top);
            Assert.StartsWith("Filtered: ", summer.FilterNote);
            Assert.DoesNotContain(year.ToString(), summer.FilterNote);
        });
    }

    [Fact]
    public void TheWindow_UsesTheRangesWords()
    {
        WpfTestHost.Run(() =>
        {
            var range = new CardRange(new DateTime(2019, 6, 1), new DateTime(2019, 8, 31));
            var window = new YearReviewWindow(new YearReview { Range = range });

            Assert.Equal("June to August 2019 in Review", window.Title);
            Assert.Contains("no plays between June and August 2019", ((TextBlock)window.FindName("EmptyText")).Text);
        });
    }
}
