using System.Runtime.InteropServices;
using Sortify.Models;
using Sortify.ViewModels;

namespace Sortify.Services;

public sealed record SearchSuggestion(string Kind, string Name, string Secondary, long Milliseconds, int Plays, string Scope = "All history")
{
    public string Details => $"{Kind} · {Secondary}".TrimEnd(' ', '·');
    public string Summary => $"{Scope}: {TimeFormat.Friendly(TimeSpan.FromMilliseconds(Milliseconds))} · {Plural.Of(Plays, "play")}";
}

public static class SearchSuggestions
{
    /// <summary>How many suggestions a box shows at once.</summary>
    public const int MaxShown = 8;

    public static IReadOnlyList<SearchSuggestion> Build(IEnumerable<PlayRecord> records)
    {
        var totals = new Dictionary<(string Kind, string Name, string Secondary), (long Ms, int Plays)>();
        foreach (var record in records)
        {
            if (record.Kind == ContentKind.Music)
            {
                Add("Artist", record.ArtistName, "");
                Add("Track", record.TrackName, record.ArtistName);
                Add("Album", record.AlbumName, record.ArtistName);
            }
            else
            {
                Add("Show", record.ShowName, record.Kind.ToString());
                Add("Episode", record.EpisodeName, record.ShowName);
            }

            void Add(string kind, string name, string secondary)
            {
                if (string.IsNullOrWhiteSpace(name))
                    return;
                ref var total = ref CollectionsMarshal.GetValueRefOrAddDefault(totals, (kind, name, secondary), out _);
                total.Ms += record.MsPlayed;
                total.Plays++;
            }
        }
        return totals.Select(x => new SearchSuggestion(x.Key.Kind, x.Key.Name, x.Key.Secondary, x.Value.Ms, x.Value.Plays)).ToArray();
    }

    public static IReadOnlyList<SearchSuggestion> FromResult(AnalysisResult result) =>
        result.Artists.Select(a => new SearchSuggestion("Artist", a.Artist, "", a.TotalMsPlayed, a.PlayCount, "Current filters"))
            .Concat(result.Tracks.Select(t => new SearchSuggestion("Track", t.Track, t.Artist, t.TotalMsPlayed, t.PlayCount, "Current filters")))
            .Concat(result.Albums.Select(a => new SearchSuggestion("Album", a.Album, a.Artist, a.TotalMsPlayed, a.PlayCount, "Current filters")))
            .Concat(result.Shows.Select(s => new SearchSuggestion("Show", s.Show, s.Kind.ToString(), s.TotalMsPlayed, s.PlayCount, "Current filters")))
            .Concat(result.Episodes.Select(e => new SearchSuggestion("Episode", e.Episode, e.Show, e.TotalMsPlayed, e.PlayCount, "Current filters")))
            .ToArray();

    public static IReadOnlyList<SearchSuggestion> Find(IEnumerable<SearchSuggestion> source, string query, string kinds)
    {
        query = query.Trim();
        if (query.Length == 0)
            return Array.Empty<SearchSuggestion>();
        string[] allowedKinds = kinds.Split(',');
        return NameMatch.Best(source.Where(x => kinds.Length == 0 || allowedKinds.Contains(x.Kind)), Rank, MaxShown,
            static (a, b) =>
            {
                int byTime = b.Milliseconds.CompareTo(a.Milliseconds);
                return byTime != 0 ? byTime : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
            });

        int Rank(SearchSuggestion item)
        {
            int nameRank = NameMatch.Rank(item.Name, query);
            if (nameRank >= 0)
                return nameRank;
            // Table filters also match the artist or show; keep direct name matches first.
            int secondaryRank = item.Kind is "Track" or "Album" or "Episode"
                ? NameMatch.Rank(item.Secondary, query) : -1;
            return secondaryRank < 0 ? -1 : secondaryRank + 4;
        }
    }
}
