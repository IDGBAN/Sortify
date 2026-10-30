namespace Sortify.Services;

/// <summary>How well a name matches what was typed into a box that suggests names.</summary>
public static class NameMatch
{
    /// <summary>0 for the whole name, 1 for its start, 2 for the start of a later word, 3 for anywhere else, -1 for no match.</summary>
    public static int Rank(string name, string query)
    {
        int at = name.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return -1;
        if (at == 0)
            return name.Length == query.Length ? 0 : 1;

        for (; at > 0; at = name.IndexOf(query, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (!char.IsLetterOrDigit(name[at - 1]))
                return 2;
        }
        return 3;
    }

    /// <summary>
    /// The <paramref name="count"/> best matches, best first: the lowest <paramref name="rank"/>,
    /// then whatever <paramref name="tieBreak"/> puts first, then the order they came in. A rank
    /// below zero is no match. Only that many are kept while looking, since a single letter
    /// matches nearly every name and only the first few are ever shown.
    /// </summary>
    public static List<T> Best<T>(IEnumerable<T> items, Func<T, int> rank, int count, Comparison<T>? tieBreak = null)
    {
        var best = new List<(T Item, int Rank)>(Math.Max(count, 0) + 1);
        if (count <= 0)
            return new List<T>();

        foreach (var item in items)
        {
            int r = rank(item);
            if (r < 0 || (best.Count == count && !Ahead(item, r, best[^1])))
                continue;

            int at = best.Count;
            while (at > 0 && Ahead(item, r, best[at - 1]))
                at--;
            best.Insert(at, (item, r));
            if (best.Count > count)
                best.RemoveAt(count);
        }
        return best.ConvertAll(b => b.Item);

        // Only a strictly better match moves ahead, so equals keep the order they came in.
        bool Ahead(T item, int r, (T Item, int Rank) kept) =>
            r != kept.Rank ? r < kept.Rank : tieBreak is not null && tieBreak(item, kept.Item) < 0;
    }
}
