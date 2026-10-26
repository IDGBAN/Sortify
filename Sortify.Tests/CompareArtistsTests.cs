using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Xunit;

namespace Sortify.Tests;

/// <summary>Compare Artists on the Trends tab.</summary>
[Collection(WpfCollection.Name)]
public class CompareArtistsTests
{
    // Six artists, each played a little less than the one before.
    private static readonly string[] Names = { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot" };

    private static readonly List<PlayRecord> Records = Names
        .SelectMany((name, i) => Enumerable.Range(0, Names.Length - i).Select(day => new PlayRecord
        {
            TrackName = "Song",
            ArtistName = name,
            AlbumName = "LP",
            MsPlayed = 200_000,
            Timestamp = new DateTime(2024, 1, 1).AddDays(day * 40),
        }))
        .ToList();

    private static readonly IReadOnlyList<ArtistStat> Artists = AnalysisEngine.Analyze(Records, new FilterOptions()).Artists;

    private static CompareArtistsViewModel Model() =>
        new(() => new CompareArtistsViewModel.Source(Records, new FilterOptions(), DayOfWeek.Sunday));

    [Fact]
    public async Task ItStartsWithTheTopThree_AndTakesAndDropsArtistsByName()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var vm = Model();
            await vm.UpdateAsync(Artists);
            Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, vm.Artists);
            Assert.Equal(3, vm.Chart.Series.Length);

            vm.Query = "del";
            Assert.Equal(new[] { "Delta" }, vm.Suggestions);
            await vm.AddCommand.ExecuteAsync(null);
            Assert.Equal("Delta", vm.Artists[^1]);
            Assert.Empty(vm.Query);
            Assert.Equal(4, vm.Chart.Series.Length);

            await vm.RemoveCommand.ExecuteAsync("Alpha");
            Assert.DoesNotContain("Alpha", vm.Artists);
            Assert.Equal(3, vm.Chart.Series.Length);

            // Later results keep what was picked; a different history starts over.
            await vm.UpdateAsync(Artists);
            Assert.DoesNotContain("Alpha", vm.Artists);
            vm.StartOver();
            await vm.UpdateAsync(Artists);
            Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, vm.Artists);
        });
    }

    [Fact]
    public async Task EquallyGoodMatches_AreSuggestedInTheOrderTheSwitchRanks()
    {
        // The three busiest artists start on the chart; "Moon Long" has more listening time
        // and "Moon Many" more plays.
        var records = new[] { "Sun A", "Sun B", "Sun C" }
            .SelectMany(name => Plays(name, 20, 1_000_000))
            .Concat(Plays("Moon Long", 2, 1_000_000))
            .Concat(Plays("Moon Many", 10, 60_000))
            .ToList();
        var result = AnalysisEngine.Analyze(records, new FilterOptions());

        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new CompareArtistsViewModel(() => new CompareArtistsViewModel.Source(records, new FilterOptions(), DayOfWeek.Sunday));

            await vm.UpdateAsync(result.ArtistsByPlayCount);
            vm.Query = "moon";
            Assert.Equal(new[] { "Moon Many", "Moon Long" }, vm.Suggestions);
            Assert.Equal(vm.Suggestions, vm.SuggestionDetails.Select(s => s.Name));
            Assert.Equal(10, vm.SuggestionDetails[0].Plays);

            await vm.UpdateAsync(result.Artists);
            Assert.Equal(new[] { "Moon Long", "Moon Many" }, vm.Suggestions);
        });

        static IEnumerable<PlayRecord> Plays(string artist, int count, int ms) => Enumerable.Range(0, count)
            .Select(day => new PlayRecord
            {
                TrackName = "Song",
                ArtistName = artist,
                AlbumName = "LP",
                MsPlayed = ms,
                Timestamp = new DateTime(2024, 1, 1).AddDays(day),
            });
    }

    [Fact]
    public async Task AFullChart_SaysSoInsteadOfTakingAnother()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var vm = Model();
            string? notice = null;
            vm.Notice += (_, message) => notice = message;
            await vm.UpdateAsync(Artists);
            foreach (var name in new[] { "Delta", "Echo" })
                await vm.AddCommand.ExecuteAsync(name);
            Assert.Equal(ArtistComparison.MaxArtists, vm.Artists.Count);

            vm.Query = "fox";
            await vm.AddCommand.ExecuteAsync(null);

            Assert.DoesNotContain("Foxtrot", vm.Artists);
            Assert.Contains("Foxtrot", notice);
        });
    }
}
