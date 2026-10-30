using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

/// <summary>Picking the best few names for a suggestion box.</summary>
public class NameMatchTests
{
    [Fact]
    public void Best_PutsTheCloserMatchFirst_ThenTheTieBreak_ThenTheOrderGiven()
    {
        var names = new[] { "Moonlight", "Blue Moon", "Moon", "Half Moon Bay", "Honeymoon", "Moonage" };
        var lengths = names.ToDictionary(n => n, n => n.Length);

        var inOrder = NameMatch.Best(names, n => NameMatch.Rank(n, "moon"), 10);
        Assert.Equal(new[] { "Moon", "Moonlight", "Moonage", "Blue Moon", "Half Moon Bay", "Honeymoon" }, inOrder);

        var shortestFirst = NameMatch.Best(names, n => NameMatch.Rank(n, "moon"), 10,
            (a, b) => lengths[a].CompareTo(lengths[b]));
        Assert.Equal(new[] { "Moon", "Moonage", "Moonlight", "Blue Moon", "Half Moon Bay", "Honeymoon" }, shortestFirst);
    }

    [Fact]
    public void Best_StopsAtTheCount_AndLeavesOutWhatDoesNotMatch()
    {
        var names = Enumerable.Range(1, 50).Select(i => $"Band {i}").Append("Solo").ToList();

        Assert.Equal(new[] { "Band 1", "Band 2", "Band 3" }, NameMatch.Best(names, n => NameMatch.Rank(n, "band"), 3));
        Assert.Empty(NameMatch.Best(names, n => NameMatch.Rank(n, "choir"), 3));
        Assert.Empty(NameMatch.Best(names, n => NameMatch.Rank(n, "band"), 0));
    }

    [Fact]
    public void Best_AgreesWithSortingEveryMatch()
    {
        var rng = new Random(7);
        for (int round = 0; round < 200; round++)
        {
            var items = Enumerable.Range(0, rng.Next(0, 40))
                .Select(i => (Id: i, Rank: rng.Next(-1, 4), Ms: rng.Next(0, 5)))
                .ToList();
            int count = rng.Next(0, 12);

            var expected = items
                .Where(x => x.Rank >= 0)
                .OrderBy(x => x.Rank)
                .ThenByDescending(x => x.Ms)
                .Take(count)
                .ToList();
            var best = NameMatch.Best(items, x => x.Rank, count, (a, b) => b.Ms.CompareTo(a.Ms));

            Assert.Equal(expected, best);
        }
    }
}
