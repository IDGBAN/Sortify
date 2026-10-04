using System.Text;
using Sortify.Models;

namespace Sortify.Services;

/// <summary>
/// Makes up a listening history to explore before a real export arrives. Every name is built
/// from invented syllables, no play carries a Spotify link, and a seed always gives the same
/// history.
/// </summary>
public static class SampleHistory
{
    /// <summary>How many days the sample covers, ending on the day before it is made.</summary>
    public const int Days = 730;

    public const int DefaultSeed = 2024;

    /// <summary>Artists in the sample, from the most played down to ones heard a handful of times.</summary>
    private const int ArtistCount = 160;

    /// <summary>
    /// How fast popularity falls from the top artist down. Around 1 gives the long tail a real
    /// history has; much higher puts nearly all the listening on the top few.
    /// </summary>
    private const double PopularityFalloff = 1.0;

    /// <summary>
    /// Flattens the very top of the curve. At 1 the favourite artist takes about a third of all
    /// listening; around 3 they take the tenth or so a real history shows.
    /// </summary>
    private const double PopularityHead = 3;

    /// <summary>How unevenly an artist's own songs are played: 0 is evenly, 1 leans hard on a few hits.</summary>
    private const double HitFalloff = 0.7;

    /// <summary>Average listening sessions on a weekday and on a weekend day.</summary>
    private const double WeekdaySessions = 3.1;
    private const double WeekendSessions = 2.4;

    /// <summary>Chance that an ordinary day has no listening at all.</summary>
    private const double QuietDayChance = 0.04;

    private const double ShuffleChance = 0.45;
    private const double PrivateSessionChance = 0.015;

    /// <summary>Chance that a weekday commute is spent on a podcast rather than music.</summary>
    private const double PodcastCommuteChance = 0.55;

    // How a music session picks what to play; the rest of the sessions are mixes.
    private const double AlbumSessionChance = 0.2;
    private const double ArtistSessionChance = 0.3;

    private const string HomeCountry = "CA";
    private static readonly string[] TripCountries = { "JP", "PT", "MX", "IS" };

    // Platform strings shaped like the ones Spotify writes, so AnalysisEngine.PlatformFamily
    // sorts them into the same device families a real export ends up in.
    private const string Desktop = "windows 10 (10.0.22631; x64)";
    private const string WebPlayer = "web_player windows 10;chrome 124.0.0.0;desktop";
    private const string Speaker = "Partner sonos_gen2 Sonos;Era 100";
    private const string Car = "android_auto (Android 14)";
    private const string AndroidPhone = "android OS 14 API 34 (Google, Pixel 8)";
    private const string IPhone = "iOS 17.4.1 (iPhone15,2)";

    // Relative chance of a session starting in each hour of the day.
    private static readonly double[] WeekdayHours =
    {
        0.3, 0.1, 0, 0, 0, 0, 0.2, 1.6, 1.8, 0.8, 0.9, 1.0,
        1.4, 1.2, 1.2, 1.3, 1.4, 1.2, 1.3, 1.6, 2.0, 2.2, 1.8, 0.9,
    };

    private static readonly double[] WeekendHours =
    {
        0.8, 0.5, 0.2, 0, 0, 0, 0, 0.1, 0.3, 0.7, 1.2, 1.5,
        1.5, 1.4, 1.4, 1.5, 1.5, 1.4, 1.4, 1.6, 1.9, 2.0, 1.8, 1.3,
    };

    /// <summary>
    /// A made-up history of <see cref="Days"/> days ending on <paramref name="lastDay"/>
    /// (yesterday when null), in time order.
    /// </summary>
    public static List<PlayRecord> Generate(int seed = DefaultSeed, DateTime? lastDay = null)
    {
        var last = DateTime.SpecifyKind((lastDay ?? DateTime.Today.AddDays(-1)).Date, DateTimeKind.Local);
        return new Generator(seed, last).Run();
    }

    private sealed record Track(string Name, string Album, int Ms, double SkipFactor);

    private sealed class Artist
    {
        public required string Name { get; init; }
        public required double Popularity { get; init; }
        public required int FoundOn { get; init; }

        /// <summary>The day it drops out of rotation, if it ever does.</summary>
        public required int GoneFrom { get; init; }

        public required int EraPeak { get; init; }
        public required double EraWidth { get; init; }
        public required double EraBoost { get; init; }
        public required double SkipRate { get; init; }
        public required List<List<Track>> Albums { get; init; }
        public required List<Track> Tracks { get; init; }
        public required double[] TrackWeights { get; init; }
    }

    private sealed class Show
    {
        public required string Name { get; init; }
        public required ContentKind Kind { get; init; }
        public required int StartsOn { get; init; }
        public required double Weight { get; init; }
        public required int EpisodeMinutes { get; init; }

        /// <summary>The episode out when the history starts; a show has a back catalogue.</summary>
        public required int FirstEpisode { get; init; }

        public Dictionary<int, string> Titles { get; } = new();
    }

    private readonly record struct SessionContext(string Country, string Platform, bool Offline, bool Private);

    private sealed class Generator
    {
        private readonly Random _rng;
        private readonly NameMaker _names;
        private readonly DateTime _first;
        private readonly List<PlayRecord> _plays = new(40_000);
        private readonly List<Artist> _artists;
        private readonly List<Show> _shows;
        private readonly Show _book;
        private readonly string _phone;

        /// <summary>When the last play ended; every new play starts after it.</summary>
        private DateTime _clock;

        private int _chapter;
        private int _weightsDay = -1;
        private double[] _weights = Array.Empty<double>();

        public Generator(int seed, DateTime lastDay)
        {
            _rng = new Random(seed);
            _names = new NameMaker(_rng);
            _first = lastDay.AddDays(-(Days - 1));
            _clock = _first;
            _phone = _rng.Next(2) == 0 ? AndroidPhone : IPhone;
            _artists = MakeArtists();
            _shows = MakeShows();
            _book = new Show
            {
                Name = _names.BookTitle(),
                Kind = ContentKind.Audiobook,
                StartsOn = 0,
                Weight = 1,
                EpisodeMinutes = 40,
                FirstEpisode = 1,
            };
        }

        public List<PlayRecord> Run()
        {
            // One stretch with no listening and one trip abroad, so the longest break, the
            // country breakdown and offline listening all have something to show.
            int breakStart = _rng.Next(150, 330), breakEnd = breakStart + _rng.Next(8, 14);
            int tripStart = _rng.Next(400, 600), tripEnd = tripStart + _rng.Next(7, 12);
            string tripCountry = TripCountries[_rng.Next(TripCountries.Length)];

            for (int day = 0; day < Days; day++)
            {
                if ((day >= breakStart && day < breakEnd) || _rng.NextDouble() < QuietDayChance)
                    continue;

                var date = _first.AddDays(day);
                bool weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                bool away = day >= tripStart && day < tripEnd;
                bool flying = away && (day == tripStart || day == tripEnd - 1);

                foreach (int hour in SessionHours(weekend))
                {
                    var start = date.AddHours(hour).AddMinutes(_rng.Next(60));
                    _clock = start > _clock ? start : _clock.AddMinutes(_rng.Next(5, 40));
                    var session = new SessionContext(
                        away ? tripCountry : HomeCountry,
                        away ? _phone : DeviceFor(hour, weekend),
                        Offline: flying,
                        Private: _rng.NextDouble() < PrivateSessionChance);

                    if (!weekend && !away && hour is 7 or 8 && _rng.NextDouble() < PodcastCommuteChance)
                        PlayEpisode(day, session);
                    else if (_chapter < BookChapters && (flying || (hour >= 21 && _rng.NextDouble() < 0.04)))
                        PlayChapter(session);
                    else
                        PlayMusic(day, session);
                }
            }
            return _plays;
        }

        private const int BookChapters = 28;

        private IEnumerable<int> SessionHours(bool weekend)
        {
            double[] weights = (double[])(weekend ? WeekendHours : WeekdayHours).Clone();
            int count = Math.Clamp(Poisson(weekend ? WeekendSessions : WeekdaySessions), 1, 6);
            var hours = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                int hour = Pick(weights);
                hours.Add(hour);
                weights[hour] = 0;
            }
            hours.Sort();
            return hours;
        }

        private string DeviceFor(int hour, bool weekend)
        {
            double roll = _rng.NextDouble();
            return hour switch
            {
                < 7 => _phone,
                < 9 when !weekend => _phone,
                < 18 when !weekend => roll < 0.7 ? Desktop : WebPlayer,
                < 18 => roll < 0.45 ? _phone : roll < 0.7 ? Car : Speaker,
                _ => roll < 0.4 ? Speaker : roll < 0.7 ? Desktop : _phone,
            };
        }

        // ---- Music -------------------------------------------------------------------------

        private void PlayMusic(int day, SessionContext session)
        {
            var weights = ArtistWeights(day);
            bool shuffled = _rng.NextDouble() < ShuffleChance;
            double roll = _rng.NextDouble();

            if (roll < AlbumSessionChance)
            {
                var artist = _artists[Pick(weights)];
                var album = artist.Albums[_rng.Next(artist.Albums.Count)];
                var order = shuffled ? album.OrderBy(_ => _rng.Next()).ToList() : album;
                int length = _rng.NextDouble() < 0.25 ? _rng.Next(3, order.Count + 1) : order.Count;
                for (int i = 0; i < length; i++)
                    PlayTrack(artist, order[i], session, shuffled, lastInSession: i == length - 1, albumOrder: !shuffled);
            }
            else if (roll < AlbumSessionChance + ArtistSessionChance)
            {
                var artist = _artists[Pick(weights)];
                int length = _rng.Next(5, 19);
                for (int i = 0; i < length; i++)
                {
                    var who = _rng.NextDouble() < 0.85 ? artist : _artists[Pick(weights)];
                    PlayTrack(who, who.Tracks[Pick(who.TrackWeights)], session, shuffled, lastInSession: i == length - 1);
                }
            }
            else
            {
                int length = _rng.Next(4, 26);
                for (int i = 0; i < length; i++)
                {
                    var who = _artists[Pick(weights)];
                    PlayTrack(who, who.Tracks[Pick(who.TrackWeights)], session, shuffled, lastInSession: i == length - 1);
                }
            }
        }

        private void PlayTrack(Artist artist, Track track, SessionContext session, bool shuffled, bool lastInSession, bool albumOrder = false)
        {
            double skipChance = artist.SkipRate * track.SkipFactor * (shuffled ? 1.4 : 1) * (albumOrder ? 0.6 : 1);
            int ms;
            string reason;
            bool skipped = false;
            if (_rng.NextDouble() < skipChance)
            {
                ms = _rng.Next(1_500, 45_000);
                reason = "fwdbtn";
                skipped = true;
            }
            else if (lastInSession && _rng.NextDouble() < 0.4)
            {
                ms = _rng.Next(15_000, track.Ms);
                reason = "endplay";
            }
            else if (_rng.NextDouble() < 0.03)
            {
                ms = _rng.Next(10_000, track.Ms);
                reason = _rng.Next(2) == 0 ? "backbtn" : "clickrow";
            }
            else
            {
                ms = track.Ms;
                reason = "trackdone";
            }

            _plays.Add(new PlayRecord
            {
                TrackName = track.Name,
                ArtistName = artist.Name,
                AlbumName = track.Album,
                MsPlayed = ms,
                Timestamp = Advance(ms),
                ReasonEnd = reason,
                Skipped = skipped,
                Platform = session.Platform,
                Country = session.Country,
                Shuffle = shuffled,
                Offline = session.Offline,
                Incognito = session.Private,
                HasPlaybackFlags = true,
            });
        }

        /// <summary>
        /// How likely each artist is to come on, on a given day: nothing before they were
        /// found or after they dropped out, a burst while they are new, and a swell around
        /// their own era. Worked out once per day and shared by that day's sessions.
        /// </summary>
        private double[] ArtistWeights(int day)
        {
            if (_weightsDay == day)
                return _weights;

            var weights = new double[_artists.Count];
            for (int i = 0; i < _artists.Count; i++)
            {
                var a = _artists[i];
                if (day < a.FoundOn || day >= a.GoneFrom)
                    continue;

                double era = (day - a.EraPeak) / a.EraWidth;
                double weight = a.Popularity * (1 + a.EraBoost * Math.Exp(-era * era));
                int sinceFound = day - a.FoundOn;
                if (sinceFound < 30)
                    weight *= 1 + 2 * (1 - sinceFound / 30.0);
                weights[i] = weight;
            }

            _weightsDay = day;
            _weights = weights;
            return weights;
        }

        private List<Artist> MakeArtists()
        {
            var artists = new List<Artist>(ArtistCount);
            for (int i = 0; i < ArtistCount; i++)
            {
                // The first few are there from day one, so the history never starts empty.
                int foundOn = i < 5 ? 0 : i < 25 ? _rng.Next(0, 45) : _rng.Next(0, Days - 20);

                // Some favourites drop out of rotation well before the end, which is what the
                // forgotten favourites on the Insights tab look for.
                int goneFrom = i is >= 3 and < 60 && foundOn < Days - 360 && _rng.NextDouble() < 0.2
                    ? _rng.Next(foundOn + 120, Days - 190)
                    : int.MaxValue;

                string name = _names.ArtistName();
                int albumCount = i < 30 ? _rng.Next(3, 6) : i < 90 ? _rng.Next(1, 4) : 1;
                var albums = new List<List<Track>>(albumCount);
                var titles = new HashSet<string>(StringComparer.Ordinal);
                var albumTitles = new HashSet<string>(StringComparer.Ordinal);
                for (int a = 0; a < albumCount; a++)
                {
                    string album = Unique(_names.AlbumTitle, albumTitles);
                    int length = _rng.Next(6, 14);
                    var tracks = new List<Track>(length);
                    for (int t = 0; t < length; t++)
                    {
                        tracks.Add(new Track(Unique(_names.TrackTitle, titles), album,
                            _rng.Next(140_000, 330_000), 0.6 + _rng.NextDouble() * 0.9));
                    }
                    albums.Add(tracks);
                }

                // A handful of songs carry each artist, as they do in a real library.
                var all = albums.SelectMany(t => t).ToList();
                var ranks = Enumerable.Range(0, all.Count).OrderBy(_ => _rng.Next()).ToArray();
                artists.Add(new Artist
                {
                    Name = name,
                    Popularity = 1 / Math.Pow(i + PopularityHead, PopularityFalloff),
                    FoundOn = foundOn,
                    GoneFrom = goneFrom,
                    EraPeak = _rng.Next(foundOn, Days),
                    EraWidth = _rng.Next(30, 160),
                    EraBoost = _rng.NextDouble() * (i < 40 ? 5 : 3),
                    SkipRate = 0.04 + _rng.NextDouble() * 0.28,
                    Albums = albums,
                    Tracks = all,
                    TrackWeights = ranks.Select(r => 1 / Math.Pow(r + 1, HitFalloff)).ToArray(),
                });
            }
            return artists;
        }

        // ---- Podcasts and the audiobook ----------------------------------------------------------

        private List<Show> MakeShows() => new()
        {
            new Show { Name = _names.ShowName(), Kind = ContentKind.Podcast, StartsOn = 0, Weight = 3, EpisodeMinutes = 55, FirstEpisode = 140 },
            new Show { Name = _names.ShowName(), Kind = ContentKind.Podcast, StartsOn = _rng.Next(100, 300), Weight = 2, EpisodeMinutes = 40, FirstEpisode = 12 },
            new Show { Name = _names.ShowName(), Kind = ContentKind.Podcast, StartsOn = _rng.Next(300, 500), Weight = 1, EpisodeMinutes = 70, FirstEpisode = 1 },
        };

        private void PlayEpisode(int day, SessionContext session)
        {
            var current = _shows.Where(s => s.StartsOn <= day).ToList();
            var show = current[Pick(current.Select(s => s.Weight).ToArray())];

            // Weekly episodes, and now and then one from the week before.
            int number = show.FirstEpisode + (day - show.StartsOn) / 7 - (_rng.NextDouble() < 0.2 ? 1 : 0);
            if (!show.Titles.TryGetValue(number, out var title))
            {
                title = $"#{number}: {_names.TrackTitle()}";
                show.Titles[number] = title;
            }

            int ms = (_rng.Next(12, show.EpisodeMinutes) * 60 + _rng.Next(60)) * 1000;
            AddSpoken(show, title, ms, ms > (show.EpisodeMinutes - 4) * 60_000, session);
        }

        private void PlayChapter(SessionContext session)
        {
            _chapter++;
            int ms = (_rng.Next(18, 42) * 60 + _rng.Next(60)) * 1000;
            AddSpoken(_book, $"Chapter {_chapter}", ms, finished: _rng.NextDouble() < 0.7, session);
        }

        private void AddSpoken(Show show, string episode, int ms, bool finished, SessionContext session)
        {
            _plays.Add(new PlayRecord
            {
                Kind = show.Kind,
                ShowName = show.Name,
                EpisodeName = episode,
                MsPlayed = ms,
                Timestamp = Advance(ms),
                ReasonEnd = finished ? "trackdone" : "endplay",
                Platform = session.Platform,
                Country = session.Country,
                Offline = session.Offline,
                Incognito = session.Private,
                HasPlaybackFlags = true,
            });
        }

        // ---- Helpers -------------------------------------------------------------------------

        /// <summary>
        /// Moves the clock past a play of <paramref name="ms"/> and returns when it ended, to the
        /// second as Spotify writes it. A short gap follows before the next play.
        /// </summary>
        private DateTime Advance(int ms)
        {
            _clock = _clock.AddMilliseconds(ms);
            var ended = new DateTime(_clock.Ticks - _clock.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Local);
            _clock = _clock.AddSeconds(_rng.Next(1, 4));
            return ended;
        }

        /// <summary>An index chosen with probability in proportion to its weight.</summary>
        private int Pick(IReadOnlyList<double> weights)
        {
            double total = 0;
            foreach (double w in weights)
                total += w;

            double roll = _rng.NextDouble() * total;
            for (int i = 0; i < weights.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0 && weights[i] > 0)
                    return i;
            }

            // Rounding can leave the roll a hair above zero; the last weighted index takes it.
            for (int i = weights.Count - 1; i >= 0; i--)
            {
                if (weights[i] > 0)
                    return i;
            }
            return 0;
        }

        private int Poisson(double mean)
        {
            double limit = Math.Exp(-mean), product = _rng.NextDouble();
            int count = 0;
            while (product > limit)
            {
                count++;
                product *= _rng.NextDouble();
            }
            return count;
        }

        private static string Unique(Func<string> make, HashSet<string> taken)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                string candidate = make();
                if (taken.Add(candidate))
                    return candidate;
            }

            string fallback = make();
            int n = 2;
            while (!taken.Add($"{fallback} {n}"))
                n++;
            return $"{fallback} {n}";
        }
    }

    /// <summary>Invented words, and titles built around them.</summary>
    private sealed class NameMaker
    {
        private static readonly string[] Onsets =
        {
            "", "b", "br", "d", "dr", "f", "g", "gr", "h", "k", "kl", "l", "m", "n", "p", "pr",
            "r", "s", "sk", "st", "t", "tr", "v", "z",
        };

        private static readonly string[] Vowels = { "a", "a", "e", "e", "i", "o", "o", "u", "ai", "ei", "ou" };
        private static readonly string[] Codas = { "l", "m", "n", "n", "r", "r", "s", "th", "nd", "x" };

        private static readonly string[] TrackPatterns =
        {
            "{0}", "{0}", "{0} {1}", "The {0}", "{0} Again", "Under {0}", "Letters to {0}",
            "Night in {0}", "{0} and {1}", "Before {0}", "Slow {0}", "All of {0}", "{0} Lights",
        };

        private static readonly string[] AlbumPatterns =
        {
            "{0}", "{0} {1}", "The {0} Sessions", "Songs for {0}", "Return to {0}", "{0} Blue",
        };

        private static readonly string[] ShowPatterns =
        {
            "The {0} Hour", "{0} Weekly", "Field Notes from {0}", "Talking {0}",
        };

        private readonly Random _rng;
        private readonly HashSet<string> _artists = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _shows = new(StringComparer.OrdinalIgnoreCase);

        public NameMaker(Random rng) => _rng = rng;

        public string ArtistName()
        {
            while (true)
            {
                double roll = _rng.NextDouble();
                string name = roll switch
                {
                    < 0.5 => Word(),
                    < 0.75 => $"{Word()} {Word()}",
                    < 0.87 => $"The {Word()}s",
                    _ => $"{Word()} & {Word()}",
                };
                if (_artists.Add(name))
                    return name;
            }
        }

        public string TrackTitle() => Fill(TrackPatterns);
        public string AlbumTitle() => Fill(AlbumPatterns);

        public string ShowName()
        {
            while (true)
            {
                string name = Fill(ShowPatterns);
                if (_shows.Add(name))
                    return name;
            }
        }

        public string BookTitle() => $"The {Word()} Chronicles";

        private string Fill(string[] patterns) =>
            string.Format(patterns[_rng.Next(patterns.Length)], Word(), Word());

        /// <summary>Two or three made-up syllables, closed by a consonant.</summary>
        private string Word()
        {
            var sb = new StringBuilder();
            int syllables = _rng.NextDouble() < 0.85 ? 2 : 3;
            for (int i = 0; i < syllables; i++)
                sb.Append(Onsets[_rng.Next(Onsets.Length)]).Append(Vowels[_rng.Next(Vowels.Length)]);
            sb.Append(Codas[_rng.Next(Codas.Length)]);
            sb[0] = char.ToUpperInvariant(sb[0]);
            return sb.ToString();
        }
    }
}
