namespace Sortify.Services;

/// <summary>A count with the word that fits it: "1 play", "2,048 plays".</summary>
public static class Plural
{
    /// <param name="many">The plural, when it isn't <paramref name="one"/> with an s on the end.</param>
    public static string Of(long count, string one, string? many = null) =>
        $"{count:N0} {(count == 1 ? one : many ?? one + "s")}";
}
