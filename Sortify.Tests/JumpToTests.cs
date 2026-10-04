using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Xunit;

namespace Sortify.Tests;

/// <summary>The Ctrl+K jump box.</summary>
public class JumpToTests
{
    [Theory]
    [InlineData("Love", "love", 0)]
    [InlineData("Lovely Day", "love", 1)]
    [InlineData("Crazy Love", "love", 2)]
    [InlineData("Glove (Remix)", "love", 3)]
    [InlineData("Glove Love", "love", 2)]
    [InlineData("Hate", "love", -1)]
    public void MatchRank_PrefersWholeNamesThenStartsThenWords(string name, string query, int expected)
    {
        Assert.Equal(expected, MainViewModel.MatchRank(name, query));
    }

    private static AnalysisResult Result()
    {
        PlayRecord Play(string track, string artist, int minutes) => new()
        {
            TrackName = track,
            ArtistName = artist,
            AlbumName = track + " (Album)",
            MsPlayed = minutes * 60_000,
            Timestamp = new DateTime(2024, 1, 1, 12, 0, 0),
        };

        return AnalysisEngine.Analyze(new[]
        {
            Play("Glove", "Band A", 50),
            Play("Love", "Band B", 5),
            Play("Lovely", "Band C", 30),
            Play("Other", "Love Club", 10),
            new PlayRecord
            {
                Kind = ContentKind.Podcast, ShowName = "Love Talk", EpisodeName = "Episode 1",
                MsPlayed = 60 * 60_000, Timestamp = new DateTime(2024, 1, 1, 12, 0, 0),
            },
        }, new FilterOptions());
    }

    [Fact]
    public void Search_RanksByMatchThenListeningTime()
    {
        var tracks = MainViewModel.SearchForJump(Result(), "love").Where(r => r.Kind == "Track").Select(r => r.Name);

        Assert.Equal(new[] { "Love", "Lovely", "Glove" }, tracks);
    }

    [Fact]
    public void Search_CoversEveryKindInOrder()
    {
        var kinds = MainViewModel.SearchForJump(Result(), "love").Select(r => r.Kind).Distinct();

        Assert.Equal(new[] { "Artist", "Track", "Album", "Podcast" }, kinds);
    }

    [Fact]
    public void Search_CapsEachKind()
    {
        Assert.Equal(1, MainViewModel.SearchForJump(Result(), "love", perKind: 1).Count(r => r.Kind == "Track"));
    }

    [Fact]
    public void Search_KeepsTheRowForTheDetailView()
    {
        var match = MainViewModel.SearchForJump(Result(), "love club").Single();

        Assert.Equal("Love Club", Assert.IsType<ArtistStat>(match.Item).Artist);
    }

    [Fact]
    public void Search_FindsNothingForABlankQuery()
    {
        Assert.Empty(MainViewModel.SearchForJump(Result(), "   "));
    }
}

[Collection(WpfCollection.Name)]
public class JumpBoxTests
{
    [Fact]
    public void WithNothingLoaded_ItSaysWhyAndStaysShut()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new MainViewModel(new AppSettings());

            Assert.False(vm.OpenJump());
            Assert.False(vm.IsJumpOpen);
            Assert.Contains("Load your history first", vm.StatusText);
        });
    }
}
