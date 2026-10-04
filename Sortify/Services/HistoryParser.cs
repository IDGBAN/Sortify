using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Sortify.Models;

namespace Sortify.Services;

/// <summary>Result of parsing a set of JSON files, including any non-fatal problems.</summary>
public sealed class ParseResult
{
    public List<PlayRecord> Records { get; } = new();
    public List<string> SkippedFiles { get; } = new();
    public List<string> Warnings { get; } = new();

    /// <summary>Plays left out because another loaded file already held them.</summary>
    public int DuplicatesRemoved { get; set; }
}

/// <summary>How far a parse has got, for a determinate progress bar and a status line.</summary>
public readonly record struct ParseProgress(int Completed, int Total, string Message)
{
    public double Percent => Total <= 0 ? 0 : Completed * 100.0 / Total;
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
    /// Finds Spotify history JSON files under <paramref name="folder"/> (recursively), or
    /// inside it when it is the ZIP Spotify sends. Prefers files matching Spotify's export
    /// naming; falls back to any top-level .json files so hand-renamed exports still work.
    /// Entries inside a ZIP come back as <see cref="ArchivePath"/> paths.
    /// </summary>
    public static IReadOnlyList<string> FindHistoryFiles(string folder)
    {
        if (ArchivePath.IsArchive(folder))
            return File.Exists(folder) ? FindArchiveEntries(folder) : Array.Empty<string>();

        if (!Directory.Exists(folder))
            return Array.Empty<string>();

        // SearchOption.AllDirectories throws on the first subfolder the user can't read,
        // which takes the whole scan down with it (a Downloads folder is enough to hit this).
        // AttributesToSkip is cleared because EnumerationOptions skips hidden files by
        // default, which the SearchOption overloads never did.
        var recursive = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
        };
        var topLevel = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };

        var named = Directory
            .EnumerateFiles(folder, "*.json", recursive)
            .Where(f => IsHistoryFileName(Path.GetFileName(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (named.Count > 0)
            return named;

        return Directory
            .EnumerateFiles(folder, "*.json", topLevel)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsHistoryFileName(string name) =>
        name.StartsWith("Streaming_History", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("StreamingHistory", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("endsong", StringComparison.OrdinalIgnoreCase);

    /// <summary>The same search as for a folder, run over the entries of a ZIP.</summary>
    private static IReadOnlyList<string> FindArchiveEntries(string archive)
    {
        try
        {
            using var zip = ZipFile.OpenRead(archive);
            var json = zip.Entries
                .Where(e => e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.FullName)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var named = json.Where(n => IsHistoryFileName(Path.GetFileName(n))).ToList();
            if (named.Count == 0)
                named = json.Where(n => n.IndexOfAny(EntrySeparators) < 0).ToList();

            return named.Select(n => ArchivePath.Combine(archive, n)).ToList();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // A damaged or half-downloaded archive is reported the same way as one with no
            // history in it; the caller says which ZIP it was.
            return Array.Empty<string>();
        }
    }

    private static readonly char[] EntrySeparators = { '/', '\\' };

    public async Task<ParseResult> ParseAsync(
        IEnumerable<string> filePaths,
        IProgress<ParseProgress>? progress = null,
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
                progress?.Report(new ParseProgress(n, paths.Count, $"Read {n} of {paths.Count} file(s)..."));
                return outcome;
            }, cancellationToken))
            .ToList();

        var sources = new List<OverlapFilter.SourceFile>(tasks.Count);
        foreach (var task in tasks)
        {
            var outcome = await task.ConfigureAwait(false);
            if (outcome.Records is { } records)
                sources.Add(new OverlapFilter.SourceFile(records, outcome.IsLegacy));
            if (outcome.SkippedFile is { } skipped)
                result.SkippedFiles.Add(skipped);
            if (outcome.Warning is { } warning)
                result.Warnings.Add(warning);
        }

        cancellationToken.ThrowIfCancellationRequested();
        result.Records.AddRange(OverlapFilter.Merge(sources, out int removed));
        result.DuplicatesRemoved = removed;
        return result;
    }

    private readonly record struct FileOutcome(
        List<PlayRecord>? Records, string? SkippedFile, string? Warning, bool IsLegacy = false);

    private static async Task<FileOutcome> ParseFileAsync(string path, StringPool pool, CancellationToken cancellationToken)
    {
        try
        {
            if (ArchivePath.TrySplit(path, out var archive, out var entryName))
            {
                // Each file gets its own handle on the archive: ZipArchive isn't safe to share
                // between the concurrent parse tasks.
                using var zip = ZipFile.OpenRead(archive);
                var zipEntry = zip.GetEntry(entryName);
                if (zipEntry is null || zipEntry.Length == 0)
                    return new FileOutcome(null, path, null);

                await using var entryStream = zipEntry.Open();
                return await ReadAsync(path, entryStream, pool, cancellationToken).ConfigureAwait(false);
            }

            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0)
                return new FileOutcome(null, path, null);

            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return await ReadAsync(path, stream, pool, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return new FileOutcome(null, null, $"Skipping invalid JSON file: {Path.GetFileName(path)} ({ex.Message})");
        }
        catch (InvalidDataException ex)
        {
            return new FileOutcome(null, null, $"Could not unpack {Path.GetFileName(path)} from the ZIP ({ex.Message})");
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

    private static async Task<FileOutcome> ReadAsync(
        string path, Stream stream, StringPool pool, CancellationToken cancellationToken)
    {
        var entries = await JsonSerializer
            .DeserializeAsync<List<SpotifyHistoryEntry>>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (entries is null)
            return new FileOutcome(null, path, null);

        var records = new List<PlayRecord>(entries.Count);
        bool anyExtended = false, anyLegacy = false;
        foreach (var entry in entries)
        {
            anyExtended |= entry.Ts is not null;
            anyLegacy |= entry.LegacyEndTime is not null;

            var record = Normalize(entry, pool);
            if (record is not null)
                records.Add(record);
        }
        return new FileOutcome(records, null, null, IsLegacy: anyLegacy && !anyExtended);
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
