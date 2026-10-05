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
}
