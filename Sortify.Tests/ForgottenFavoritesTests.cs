using Sortify.Models;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

/// <summary>Tracks and artists played a lot that then went quiet.</summary>
public class ForgottenFavoritesTests
{
    private static readonly DateTime End = new(2025, 6, 30, 20, 0, 0);

    private static IEnumerable<PlayRecord> Plays(string track, string artist, int count, DateTime last) =>
        Enumerable.Range(0, count).Select(i => new PlayRecord
        {
            TrackName = track,
            ArtistName = artist,
            AlbumName = "Album",
            MsPlayed = 60_000,
            Timestamp = last.AddDays(-i),
        });

    private static AnalysisResult Analyze(params IEnumerable<PlayRecord>[] groups) =>
        AnalysisEngine.Analyze(groups.SelectMany(g => g).ToList(), new FilterOptions());

    [Fact]
    public void AFavoriteThatWentQuiet_IsListed()
    {
        var result = Analyze(
            Plays("Old Love", "Band", 25, End.AddDays(-200)),
            Plays("Still Playing", "Band", 25, End));

        Assert.Equal(new[] { "Old Love" }, result.ForgottenTracks.Select(t => t.Track));
    }

    [Fact]
    public void QuietIsMeasuredFromTheEndOfTheHistoryNotFromToday()
    {
        // Everything here is years old, but the history ends right after it.
        var then = new DateTime(2019, 1, 1);
        var result = Analyze(Plays("Then", "Band", 25, then), Plays("Last", "Other", 1, then.AddDays(10)));

        Assert.Empty(result.ForgottenTracks);
    }

    [Fact]
    public void ARarelyPlayedTrack_IsNotAFavorite()
    {
        var result = Analyze(
            Plays("Passing", "Band", AnalysisEngine.ForgottenMinTrackPlays - 1, End.AddDays(-300)),
            Plays("Now", "Other", 1, End));

        Assert.Empty(result.ForgottenTracks);
    }

    [Fact]
    public void JustInsideTheQuietStretch_StillCounts()
    {
        var result = Analyze(
            Plays("Edge", "Band", 30, End.AddDays(-AnalysisEngine.ForgottenAfterDays).AddMinutes(-1)),
            Plays("Now", "Other", 1, End));

        Assert.Single(result.ForgottenTracks);
    }

    [Fact]
    public void ArtistsNeedMorePlays_AndComeMostPlayedFirst()
    {
        var result = Analyze(
            Plays("A", "Big Band", 80, End.AddDays(-400)),
            Plays("B", "Bigger Band", 120, End.AddDays(-400)),
            Plays("C", "Small Band", 30, End.AddDays(-400)),
            Plays("Now", "Current", 1, End));

        Assert.Equal(new[] { "Bigger Band", "Big Band" }, result.ForgottenArtists.Select(a => a.Artist));
        Assert.Equal(new[] { "B", "A", "C" }, result.ForgottenTracks.Select(t => t.Track));
    }

    [Fact]
    public void TheListIsCapped()
    {
        var groups = Enumerable.Range(0, AnalysisEngine.ForgottenMaxRows + 10)
            .Select(i => Plays($"Song {i}", "Band", 21, End.AddDays(-400)))
            .Append(Plays("Now", "Other", 1, End))
            .ToArray();

        Assert.Equal(AnalysisEngine.ForgottenMaxRows, Analyze(groups).ForgottenTracks.Count);
    }
}
