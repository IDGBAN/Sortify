using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Sortify.Models;

namespace Sortify.Services;

/// <summary>Result of parsing a set of JSON files, including any non-fatal problems.</summary>
public sealed class ParseResult
{
    public List<PlayRecord> Records { get; } = new();
    public List<string> SkippedFiles { get; } = new();
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// Reads selected Spotify streaming history JSON files into normalized PlayRecords.
/// Handles both the extended history and the older account-data format. Empty or
/// invalid files are skipped gracefully (mirrors the original Python behavior).
/// </summary>
public sealed class HistoryParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Finds Spotify history JSON files under <paramref name="folder"/> (recursively).
    /// Prefers files matching Spotify's export naming; falls back to any top-level
    /// .json files so hand-renamed exports still work.
    /// </summary>
    public static IReadOnlyList<string> FindHistoryFiles(string folder)
    {
        if (!Directory.Exists(folder))
            return Array.Empty<string>();

        var named = Directory
            .EnumerateFiles(folder, "*.json", SearchOption.AllDirectories)
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return name.StartsWith("Streaming_History", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("StreamingHistory", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("endsong", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (named.Count > 0)
            return named;

        return Directory
            .EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ParseResult> ParseAsync(
        IEnumerable<string> filePaths,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ParseResult();
        var paths = filePaths.ToList();
        if (paths.Count == 0)
            return result;

        int completed = 0;
        var pool = new StringPool();

        // Parse files concurrently; each task returns its own outcome and the results are
        // merged in selection order below, so the output is deterministic.
        var tasks = paths
            .Select(path => Task.Run(async () =>
            {
                var outcome = await ParseFileAsync(path, pool, cancellationToken).ConfigureAwait(false);
                int n = Interlocked.Increment(ref completed);
                progress?.Report($"Read {n} of {paths.Count} file(s)...");
                return outcome;
            }, cancellationToken))
            .ToList();

        foreach (var task in tasks)
        {
            var outcome = await task.ConfigureAwait(false);
            if (outcome.Records is { } records)
            {
                result.Records.EnsureCapacity(result.Records.Count + records.Count);
                result.Records.AddRange(records);
            }
            if (outcome.SkippedFile is { } skipped)
                result.SkippedFiles.Add(skipped);
            if (outcome.Warning is { } warning)
                result.Warnings.Add(warning);
        }

        return result;
    }

    private readonly record struct FileOutcome(List<PlayRecord>? Records, string? SkippedFile, string? Warning);

    private static async Task<FileOutcome> ParseFileAsync(string path, StringPool pool, CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0)
                return new FileOutcome(null, path, null);

            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var entries = await JsonSerializer
                .DeserializeAsync<List<SpotifyHistoryEntry>>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (entries is null)
                return new FileOutcome(null, path, null);

            var records = new List<PlayRecord>(entries.Count);
            foreach (var entry in entries)
            {
                var record = Normalize(entry, pool);
                if (record is not null)
                    records.Add(record);
            }
            return new FileOutcome(records, null, null);
        }
        catch (JsonException ex)
        {
            return new FileOutcome(null, null, $"Skipping invalid JSON file: {Path.GetFileName(path)} ({ex.Message})");
        }
        catch (NotSupportedException ex)
        {
            return new FileOutcome(null, null, $"Skipping unsupported JSON file: {Path.GetFileName(path)} ({ex.Message})");
        }
        catch (IOException ex)
        {
            return new FileOutcome(null, null, $"Could not read {Path.GetFileName(path)} ({ex.Message})");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new FileOutcome(null, null, $"Access denied to {Path.GetFileName(path)} ({ex.Message})");
        }
    }

    /// <summary>
    /// reason_end values that mean the user moved on deliberately. Spotify's own "skipped"
    /// flag is absent (null) across large stretches of real exports, so it alone badly
    /// under-reports skips; these codes are the fallback signal.
    /// </summary>
    private static bool IsSkipReason(string? reasonEnd)
        => string.Equals(reasonEnd, "fwdbtn", StringComparison.OrdinalIgnoreCase);

    private static PlayRecord? Normalize(SpotifyHistoryEntry entry, StringPool pool)
    {
        string? trackName = FirstNonEmpty(entry.TrackName, entry.LegacyTrackName);
        string? artistName = FirstNonEmpty(entry.ArtistName, entry.LegacyArtistName);

        // Rows carry exactly one kind of content; music first, then podcast, then audiobook.
        ContentKind kind;
        string show = string.Empty, episode = string.Empty, uri;

        if (trackName is not null || artistName is not null)
        {
            kind = ContentKind.Music;
            uri = entry.TrackUri ?? string.Empty;
        }
        else if (FirstNonEmpty(entry.EpisodeName, entry.EpisodeShowName) is not null)
        {
            kind = ContentKind.Podcast;
            show = entry.EpisodeShowName ?? "Unknown Show";
            episode = entry.EpisodeName ?? "Unknown Episode";
            uri = entry.EpisodeUri ?? string.Empty;
        }
        else if (FirstNonEmpty(entry.AudiobookTitle, entry.AudiobookChapterTitle) is not null)
        {
            kind = ContentKind.Audiobook;
            show = entry.AudiobookTitle ?? "Unknown Audiobook";
            episode = entry.AudiobookChapterTitle ?? "Unknown Chapter";
            uri = entry.AudiobookUri ?? string.Empty;
        }
        else
        {
            // No usable metadata of any kind.
            return null;
        }

        long ms = entry.MsPlayed != 0 ? entry.MsPlayed : entry.LegacyMsPlayed ?? 0;

        // Spotify timestamps are UTC; convert to local time so hour-of-day and
        // day-of-week charts and filters reflect the user's clock, not UTC.
        DateTime timestamp = DateTime.MinValue;
        if (!string.IsNullOrEmpty(entry.Ts) &&
            DateTime.TryParse(entry.Ts, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            timestamp = parsed.ToLocalTime();
        }
        else if (!string.IsNullOrEmpty(entry.LegacyEndTime) &&
                 DateTime.TryParseExact(entry.LegacyEndTime, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture,
                     DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var legacyParsed))
        {
            timestamp = legacyParsed.ToLocalTime();
        }

        return new PlayRecord
        {
            TrackName = trackName ?? "Unknown Track",
            ArtistName = artistName ?? "Unknown Artist",
            AlbumName = string.IsNullOrWhiteSpace(entry.AlbumName) ? "Unknown Album" : entry.AlbumName!,
            MsPlayed = ms < 0 ? 0 : (int)Math.Min(ms, int.MaxValue),
            Timestamp = timestamp,
            ReasonEnd = entry.ReasonEnd,
            Skipped = entry.Skipped ?? IsSkipReason(entry.ReasonEnd),
            Kind = kind,
            ShowName = show,
            EpisodeName = episode,
            Uri = uri,
            // Platform and country repeat across nearly every row, so pool them instead of
            // holding one string instance per play.
            Platform = pool.Get(entry.Platform),
            Country = pool.Get(entry.ConnCountry),
            Shuffle = entry.Shuffle ?? false,
            Offline = entry.Offline ?? false,
            Incognito = entry.IncognitoMode ?? false,
            HasPlaybackFlags = entry.Shuffle is not null || entry.Offline is not null || entry.IncognitoMode is not null,
        };
    }

    private static string? FirstNonEmpty(string? a, string? b)
        => !string.IsNullOrWhiteSpace(a) ? a : !string.IsNullOrWhiteSpace(b) ? b : null;

    /// <summary>
    /// Deduplicates the handful of distinct platform/country strings that repeat across
    /// every record. Shared across the concurrent per-file parse tasks, so it must be
    /// thread-safe.
    /// </summary>
    private sealed class StringPool
    {
        private readonly ConcurrentDictionary<string, string> _pool = new(StringComparer.Ordinal);

        public string Get(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return _pool.GetOrAdd(value, static v => v);
        }
    }
}
