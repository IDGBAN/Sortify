using System.Globalization;

namespace Sortify.Tests;

/// <summary>
/// Runs a test under a culture that formats numbers and times differently from en-US, so
/// output that is meant to be culture-invariant actually gets checked. CurrentCulture flows
/// with the async context, so this is safe across awaits and doesn't leak into other tests.
/// </summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    private CultureScope(CultureInfo culture) => CultureInfo.CurrentCulture = culture;

    /// <summary>Decimal comma and a dot for the time separator, like de-DE and fi-FI between them.</summary>
    public static CultureScope Unusual()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = ".";
        culture.DateTimeFormat.TimeSeparator = ".";
        return new CultureScope(culture);
    }

    public void Dispose() => CultureInfo.CurrentCulture = _previous;
}
