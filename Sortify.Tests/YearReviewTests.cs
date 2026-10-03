using System.Windows;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>The shareable year-in-review card.</summary>
public class YearReviewBuilderTests
{
    private static PlayRecord Play(string track, string artist, int minutes, DateTime when) => new()
    {
        TrackName = track,
        ArtistName = artist,
        AlbumName = "Album",
        MsPlayed = minutes * 60_000,
        Timestamp = when,
    };

    private static readonly List<PlayRecord> Records = new()
    {
        Play("Old Song", "Old Friend", 60, new DateTime(2023, 5, 1, 12, 0, 0)),
        Play("Old Song", "Old Friend", 30, new DateTime(2024, 2, 1, 12, 0, 0)),
        Play("Hit", "New Find", 90, new DateTime(2024, 3, 10, 12, 0, 0)),
        Play("Hit", "New Find", 90, new DateTime(2024, 3, 11, 12, 0, 0)),
        Play("Deep Cut", "Another", 20, new DateTime(2024, 7, 4, 12, 0, 0)),
        Play("Next Year", "Later", 60, new DateTime(2025, 1, 1, 12, 0, 0)),
    };

    private static YearReview Build(FilterOptions? filter = null) =>
        YearReviewBuilder.Build(Records, filter ?? new FilterOptions(), 2024, string.Empty);

    [Fact]
    public void CountsOnlyThatYear()
    {
        var review = Build();

        Assert.Equal(4, review.Plays);
        Assert.Equal(230, review.TotalMinutes);
        Assert.Equal(3, review.Artists);
    }

    [Fact]
    public void NewArtistsAreJudgedAgainstTheWholeHistory()
    {
        // Old Friend was first heard in 2023, so only New Find and Another are new in 2024.
        Assert.Equal(2, Build().NewArtists);
    }

    [Fact]
    public void ListsTheTopArtistsAndTracksWithTheirArtist()
    {
        var review = Build();

        Assert.Equal(new[] { "New Find", "Old Friend", "Another" }, review.TopArtists.Select(a => a.Name));
        var top = review.TopTracks[0];
        Assert.Equal((1, "Hit", "New Find", "3 h"), (top.Rank, top.Name, top.Secondary, top.Time));
    }

    [Fact]
    public void NamesTheBiggestMonthAndDay()
    {
        var review = Build();

        Assert.Equal(new DateTime(2024, 3, 1), review.BiggestMonth);
        Assert.EndsWith(", 3 hours", review.BiggestMonthText);
        Assert.Equal(new DateTime(2024, 3, 10), review.BiggestDay);
        Assert.Equal(2, review.LongestStreakDays);
    }

    [Fact]
    public void NamesTheMonthInEnglishOnAnyCalendar()
    {
        // ar-SA counts months on the Hijri calendar, where March 2024 is mostly Ramadan.
        using var _ = CultureScope.Named("ar-SA");

        var review = Build();

        Assert.StartsWith("March, ", review.BiggestMonthText);
        Assert.StartsWith("March 10, ", review.BiggestDayText);
    }

    [Fact]
    public void TheSidebarsOtherFiltersApply_ButNotItsDates()
    {
        var filter = new FilterOptions { StartDate = new DateTime(2025, 1, 1) };
        filter.ExcludedArtists.Add("New Find");

        var review = Build(filter);

        Assert.Equal(2, review.Plays);
        Assert.DoesNotContain(review.TopArtists, a => a.Name == "New Find");
    }

    [Fact]
    public void AYearWithNothingInItIsEmpty()
    {
        var review = YearReviewBuilder.Build(Records, new FilterOptions(), 2019, string.Empty);

        Assert.Equal(0, review.Plays);
        Assert.Empty(review.TopTracks);
        Assert.Equal(string.Empty, review.BiggestMonthText);
    }

    [Fact]
    public void TheFilterNoteLeavesTheDatesOut()
    {
        var vm = new FilterViewModel { MinSeconds = 30, StartDate = new DateTime(2024, 1, 1) };

        Assert.Equal(new[] { "Min 30s" }, vm.Describe(includeDates: false));
        Assert.Equal(2, vm.Describe().Count());
    }

    [Fact]
    public void ImagesCanBeRenderedAtTheirOwnSize()
    {
        Assert.Equal(1, ImageExporter.ScaleFor(1080, 1350, scale: 1));
        Assert.Equal(2, ImageExporter.ScaleFor(400, 300));
    }
}

[Collection(WpfCollection.Name)]
public class YearReviewViewTests
{
    private static YearReview Review() => YearReviewBuilder.Build(new[]
    {
        new PlayRecord { TrackName = "A rather long track name that has to be cut short somewhere", ArtistName = "Band", MsPlayed = 3_600_000, Timestamp = new DateTime(2024, 3, 1, 9, 0, 0) },
    }, new FilterOptions(), 2024, "Filtered: Min 30s");

    [Fact]
    public void TheCardRendersAtFullSize()
    {
        WpfTestHost.Run(() =>
        {
            var card = new YearReviewCard(Review());
            card.Measure(new Size(card.Width, card.Height));
            card.Arrange(new Rect(0, 0, card.Width, card.Height));
            card.UpdateLayout();

            var bitmap = ImageExporter.Render(card, null, scale: 1);

            Assert.NotNull(bitmap);
            Assert.Equal((1080, 1350), (bitmap!.PixelWidth, bitmap.PixelHeight));
        });
    }

    [Fact]
    public void AnEmptyYearSaysSoAndOffersNothingToSave()
    {
        WpfTestHost.Run(() =>
        {
            var window = new YearReviewWindow(new YearReview { Year = 2019 });

            Assert.False(((System.Windows.Controls.Button)window.FindName("SaveButton")).IsEnabled);
            Assert.Contains("no plays in 2019", ((System.Windows.Controls.TextBlock)window.FindName("EmptyText")).Text);
        });
    }

    [Fact]
    public void OnlyAYearsBreakdownOffersTheImage()
    {
        WpfTestHost.Run(() =>
        {
            Task Open(int year, Window owner) => Task.CompletedTask;
            var year = new DetailWindow(new DetailResult { Scope = DetailScope.Year, Title = "2024", PlayCount = 1 }) { OpenYearReview = Open };
            var artist = new DetailWindow(new DetailResult { Scope = DetailScope.Artist, Title = "Band", PlayCount = 1 }) { OpenYearReview = Open };
            var noCallback = new DetailWindow(new DetailResult { Scope = DetailScope.Year, Title = "2024", PlayCount = 1 });

            Assert.Equal(Visibility.Visible, ((UIElement)year.FindName("YearReviewButton")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((UIElement)artist.FindName("YearReviewButton")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((UIElement)noCallback.FindName("YearReviewButton")).Visibility);
        });
    }
}
