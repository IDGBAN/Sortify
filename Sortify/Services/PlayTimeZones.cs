using System.Security;

namespace Sortify.Services;

/// <summary>
/// Turns Spotify's UTC play times into local ones: in this PC's time zone, as Sortify always
/// has, or in the zone of the country each play was streamed from, with a chosen zone for
/// countries that span several and for plays that record no country.
/// </summary>
public sealed class PlayTimeZones
{
    /// <summary>Bump when the country table changes, so times converted with the old one are read again.</summary>
    private const int TableVersion = 1;

    public static PlayTimeZones ThisPc { get; } = new(null);

    private readonly TimeZoneInfo? _fallback;
    private readonly Dictionary<string, TimeZoneInfo?> _byCountry = new(StringComparer.OrdinalIgnoreCase);

    private PlayTimeZones(TimeZoneInfo? fallback) => _fallback = fallback;

    /// <summary>Each play in its country's zone where the country has one, and in <paramref name="fallback"/> where it doesn't.</summary>
    public static PlayTimeZones FollowCountry(TimeZoneInfo fallback) => new(fallback);

    /// <summary>The conversion the settings ask for.</summary>
    public static PlayTimeZones From(AppSettings settings) => settings.FollowCountryTimeZones
        ? FollowCountry(Find(settings.FallbackTimeZoneId) ?? TimeZoneInfo.Local)
        : ThisPc;

    public bool FollowsCountry => _fallback is not null;

    /// <summary>
    /// Part of the record cache's key, since cached times were converted as the export was
    /// read. For this PC it is the zone id alone, which is what the key has always held.
    /// </summary>
    public string CacheKey => _fallback is null ? TimeZoneInfo.Local.Id : $"country|{_fallback.Id}|{TableVersion}";

    /// <summary>
    /// The kind converted times carry. This PC's are Local, as they always were. A country's
    /// are left Unspecified: they belong to no one zone, and an export shouldn't stamp them
    /// with this PC's offset.
    /// </summary>
    public DateTimeKind Kind => _fallback is null ? DateTimeKind.Local : DateTimeKind.Unspecified;

    /// <summary>The local time of a play that ended at <paramref name="utc"/>, streamed from <paramref name="country"/>.</summary>
    public DateTime ToLocal(DateTime utc, string? country)
    {
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        if (_fallback is null)
            return utc.ToLocalTime();

        var zone = ZoneFor(country) ?? _fallback;
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, zone), DateTimeKind.Unspecified);
    }

    private TimeZoneInfo? ZoneFor(string? country)
    {
        if (string.IsNullOrEmpty(country))
            return null;
        if (!_byCountry.TryGetValue(country, out var zone))
        {
            zone = SingleZoneCountries.TryGetValue(country, out var id) ? Find(id) : null;
            _byCountry[country] = zone;
        }
        return zone;
    }

    /// <summary>
    /// A zone by id, Windows or IANA. Null when this system doesn't know it: Windows only
    /// takes IANA ids where it has ICU, and older builds lack some newer zones.
    /// </summary>
    internal static TimeZoneInfo? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// Countries whose people all keep one clock, by the two-letter code Spotify records,
    /// to an IANA zone. A few have a small region on another clock and take the zone nearly
    /// everyone uses: Spain's Canary Islands and Portugal's Azores are an hour behind the
    /// mainland, Chile's Magallanes keeps summer time all year, and Ecuador's Galapagos, New
    /// Zealand's Chatham Islands and Papua New Guinea's Bougainville differ too. Countries
    /// that span several zones are left
    /// out on purpose, so they get the fallback: the US, Canada, Mexico, Brazil, Russia,
    /// Australia, Indonesia, Kazakhstan, Mongolia, the DR Congo, Greenland, Kiribati,
    /// Micronesia and French Polynesia.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> SingleZoneCountries =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Europe
            ["AD"] = "Europe/Andorra",
            ["AL"] = "Europe/Tirane",
            ["AT"] = "Europe/Vienna",
            ["AX"] = "Europe/Mariehamn",
            ["BA"] = "Europe/Sarajevo",
            ["BE"] = "Europe/Brussels",
            ["BG"] = "Europe/Sofia",
            ["BY"] = "Europe/Minsk",
            ["CH"] = "Europe/Zurich",
            ["CY"] = "Asia/Nicosia",
            ["CZ"] = "Europe/Prague",
            ["DE"] = "Europe/Berlin",
            ["DK"] = "Europe/Copenhagen",
            ["EE"] = "Europe/Tallinn",
            ["ES"] = "Europe/Madrid",
            ["FI"] = "Europe/Helsinki",
            ["FO"] = "Atlantic/Faroe",
            ["FR"] = "Europe/Paris",
            ["GB"] = "Europe/London",
            ["GG"] = "Europe/Guernsey",
            ["GI"] = "Europe/Gibraltar",
            ["GR"] = "Europe/Athens",
            ["HR"] = "Europe/Zagreb",
            ["HU"] = "Europe/Budapest",
            ["IE"] = "Europe/Dublin",
            ["IM"] = "Europe/Isle_of_Man",
            ["IS"] = "Atlantic/Reykjavik",
            ["IT"] = "Europe/Rome",
            ["JE"] = "Europe/Jersey",
            ["LI"] = "Europe/Vaduz",
            ["LT"] = "Europe/Vilnius",
            ["LU"] = "Europe/Luxembourg",
            ["LV"] = "Europe/Riga",
            ["MC"] = "Europe/Monaco",
            ["MD"] = "Europe/Chisinau",
            ["ME"] = "Europe/Podgorica",
            ["MK"] = "Europe/Skopje",
            ["MT"] = "Europe/Malta",
            ["NL"] = "Europe/Amsterdam",
            ["NO"] = "Europe/Oslo",
            ["PL"] = "Europe/Warsaw",
            ["PT"] = "Europe/Lisbon",
            ["RO"] = "Europe/Bucharest",
            ["RS"] = "Europe/Belgrade",
            ["SE"] = "Europe/Stockholm",
            ["SI"] = "Europe/Ljubljana",
            ["SK"] = "Europe/Bratislava",
            ["SM"] = "Europe/San_Marino",
            ["TR"] = "Europe/Istanbul",
            // The older name: Windows builds with an older ICU don't know Europe/Kyiv.
            ["UA"] = "Europe/Kiev",
            ["VA"] = "Europe/Vatican",
            ["XK"] = "Europe/Belgrade",

            // Caucasus and the Middle East
            ["AE"] = "Asia/Dubai",
            ["AM"] = "Asia/Yerevan",
            ["AZ"] = "Asia/Baku",
            ["BH"] = "Asia/Bahrain",
            ["GE"] = "Asia/Tbilisi",
            ["IL"] = "Asia/Jerusalem",
            ["IQ"] = "Asia/Baghdad",
            ["IR"] = "Asia/Tehran",
            ["JO"] = "Asia/Amman",
            ["KW"] = "Asia/Kuwait",
            ["LB"] = "Asia/Beirut",
            ["OM"] = "Asia/Muscat",
            ["PS"] = "Asia/Hebron",
            ["QA"] = "Asia/Qatar",
            ["SA"] = "Asia/Riyadh",
            ["SY"] = "Asia/Damascus",
            ["YE"] = "Asia/Aden",

            // Asia
            ["AF"] = "Asia/Kabul",
            ["BD"] = "Asia/Dhaka",
            ["BN"] = "Asia/Brunei",
            ["BT"] = "Asia/Thimphu",
            ["CN"] = "Asia/Shanghai",
            ["HK"] = "Asia/Hong_Kong",
            ["IN"] = "Asia/Kolkata",
            ["JP"] = "Asia/Tokyo",
            ["KG"] = "Asia/Bishkek",
            ["KH"] = "Asia/Phnom_Penh",
            ["KP"] = "Asia/Pyongyang",
            ["KR"] = "Asia/Seoul",
            ["LA"] = "Asia/Vientiane",
            ["LK"] = "Asia/Colombo",
            ["MM"] = "Asia/Yangon",
            ["MO"] = "Asia/Macau",
            ["MV"] = "Indian/Maldives",
            ["MY"] = "Asia/Kuala_Lumpur",
            ["NP"] = "Asia/Kathmandu",
            ["PH"] = "Asia/Manila",
            ["PK"] = "Asia/Karachi",
            ["SG"] = "Asia/Singapore",
            ["TH"] = "Asia/Bangkok",
            ["TJ"] = "Asia/Dushanbe",
            ["TL"] = "Asia/Dili",
            ["TM"] = "Asia/Ashgabat",
            ["TW"] = "Asia/Taipei",
            ["UZ"] = "Asia/Tashkent",
            ["VN"] = "Asia/Ho_Chi_Minh",

            // Africa
            ["AO"] = "Africa/Luanda",
            ["BF"] = "Africa/Ouagadougou",
            ["BI"] = "Africa/Bujumbura",
            ["BJ"] = "Africa/Porto-Novo",
            ["BW"] = "Africa/Gaborone",
            ["CF"] = "Africa/Bangui",
            ["CG"] = "Africa/Brazzaville",
            ["CI"] = "Africa/Abidjan",
            ["CM"] = "Africa/Douala",
            ["CV"] = "Atlantic/Cape_Verde",
            ["DJ"] = "Africa/Djibouti",
            ["DZ"] = "Africa/Algiers",
            ["EG"] = "Africa/Cairo",
            ["EH"] = "Africa/El_Aaiun",
            ["ER"] = "Africa/Asmara",
            ["ET"] = "Africa/Addis_Ababa",
            ["GA"] = "Africa/Libreville",
            ["GH"] = "Africa/Accra",
            ["GM"] = "Africa/Banjul",
            ["GN"] = "Africa/Conakry",
            ["GQ"] = "Africa/Malabo",
            ["GW"] = "Africa/Bissau",
            ["KE"] = "Africa/Nairobi",
            ["KM"] = "Indian/Comoro",
            ["LR"] = "Africa/Monrovia",
            ["LS"] = "Africa/Maseru",
            ["LY"] = "Africa/Tripoli",
            ["MA"] = "Africa/Casablanca",
            ["MG"] = "Indian/Antananarivo",
            ["ML"] = "Africa/Bamako",
            ["MR"] = "Africa/Nouakchott",
            ["MU"] = "Indian/Mauritius",
            ["MW"] = "Africa/Blantyre",
            ["MZ"] = "Africa/Maputo",
            ["NA"] = "Africa/Windhoek",
            ["NE"] = "Africa/Niamey",
            ["NG"] = "Africa/Lagos",
            ["RE"] = "Indian/Reunion",
            ["RW"] = "Africa/Kigali",
            ["SC"] = "Indian/Mahe",
            ["SD"] = "Africa/Khartoum",
            ["SL"] = "Africa/Freetown",
            ["SN"] = "Africa/Dakar",
            ["SO"] = "Africa/Mogadishu",
            ["SS"] = "Africa/Juba",
            ["ST"] = "Africa/Sao_Tome",
            ["SZ"] = "Africa/Mbabane",
            ["TD"] = "Africa/Ndjamena",
            ["TG"] = "Africa/Lome",
            ["TN"] = "Africa/Tunis",
            ["TZ"] = "Africa/Dar_es_Salaam",
            ["UG"] = "Africa/Kampala",
            ["YT"] = "Indian/Mayotte",
            ["ZA"] = "Africa/Johannesburg",
            ["ZM"] = "Africa/Lusaka",
            ["ZW"] = "Africa/Harare",

            // The Americas
            ["AG"] = "America/Antigua",
            ["AR"] = "America/Argentina/Buenos_Aires",
            ["AW"] = "America/Aruba",
            ["BB"] = "America/Barbados",
            ["BM"] = "Atlantic/Bermuda",
            ["BO"] = "America/La_Paz",
            ["BS"] = "America/Nassau",
            ["BZ"] = "America/Belize",
            ["CL"] = "America/Santiago",
            ["CO"] = "America/Bogota",
            ["CR"] = "America/Costa_Rica",
            ["CU"] = "America/Havana",
            ["CW"] = "America/Curacao",
            ["DM"] = "America/Dominica",
            ["DO"] = "America/Santo_Domingo",
            ["EC"] = "America/Guayaquil",
            ["GD"] = "America/Grenada",
            ["GF"] = "America/Cayenne",
            ["GP"] = "America/Guadeloupe",
            ["GT"] = "America/Guatemala",
            ["GY"] = "America/Guyana",
            ["HN"] = "America/Tegucigalpa",
            ["HT"] = "America/Port-au-Prince",
            ["JM"] = "America/Jamaica",
            ["KN"] = "America/St_Kitts",
            ["KY"] = "America/Cayman",
            ["LC"] = "America/St_Lucia",
            ["MQ"] = "America/Martinique",
            ["NI"] = "America/Managua",
            ["PA"] = "America/Panama",
            ["PE"] = "America/Lima",
            ["PR"] = "America/Puerto_Rico",
            ["PY"] = "America/Asuncion",
            ["SR"] = "America/Paramaribo",
            ["SV"] = "America/El_Salvador",
            ["TC"] = "America/Grand_Turk",
            ["TT"] = "America/Port_of_Spain",
            ["UY"] = "America/Montevideo",
            ["VC"] = "America/St_Vincent",
            ["VE"] = "America/Caracas",
            ["VG"] = "America/Tortola",
            ["VI"] = "America/St_Thomas",

            // Oceania
            ["AS"] = "Pacific/Pago_Pago",
            ["FJ"] = "Pacific/Fiji",
            ["GU"] = "Pacific/Guam",
            ["MH"] = "Pacific/Majuro",
            ["MP"] = "Pacific/Saipan",
            ["NC"] = "Pacific/Noumea",
            ["NR"] = "Pacific/Nauru",
            ["NZ"] = "Pacific/Auckland",
            ["PG"] = "Pacific/Port_Moresby",
            ["PW"] = "Pacific/Palau",
            ["SB"] = "Pacific/Guadalcanal",
            ["TO"] = "Pacific/Tongatapu",
            ["TV"] = "Pacific/Funafuti",
            ["VU"] = "Pacific/Efate",
            ["WS"] = "Pacific/Apia",
        };
}
