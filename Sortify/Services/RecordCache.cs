using System.IO;
using System.Text;
using Sortify.Models;

namespace Sortify.Services;

/// <summary>
/// Caches parsed <see cref="PlayRecord"/>s in a compact binary file so reopening the same
/// export skips JSON parsing entirely. The cache is keyed by the exact set of source files
/// and their sizes and write times, so editing, adding or removing a file invalidates it.
/// </summary>
public static class RecordCache
{
    /// <summary>Bumped whenever the record layout changes, so stale caches are ignored.</summary>
    private const int FormatVersion = 1;

    /// <summary>
    /// Upper bounds on the two counts read out of the file header, so a truncated or
    /// corrupt cache asks for a plausible allocation rather than an enormous one. Both are
    /// far past any real export: Spotify's own limit is a few hundred thousand plays, and
    /// the pool only ever holds device and country strings.
    /// </summary>
    private const int MaxRecords = 50_000_000;
    private const int MaxPoolEntries = 100_000;

    private static readonly byte[] Magic = "SRTFY\0"u8.ToArray();

    /// <summary>%LOCALAPPDATA%\Sortify — created on demand.</summary>
    public static string CacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sortify");

    private static string CacheFile => Path.Combine(CacheDirectory, "records.cache");

    /// <summary>
    /// Fingerprints the source files. Two runs over an unchanged export produce the same
    /// key; any change to the file set, a file's length or its write time produces a
    /// different one. The local time zone is part of it too, because the cached timestamps
    /// were already converted to local time when the export was parsed.
    /// </summary>
    internal static string BuildKey(IEnumerable<string> filePaths)
    {
        var parts = filePaths
            .Select(p =>
            {
                var info = new FileInfo(p);
                long length = info.Exists ? info.Length : -1;
                long ticks = info.Exists ? info.LastWriteTimeUtc.Ticks : -1;
                return $"{info.FullName.ToLowerInvariant()}|{length}|{ticks}";
            })
            .OrderBy(s => s, StringComparer.Ordinal)
            .Prepend($"tz|{TimeZoneInfo.Local.Id}");

        var joined = string.Join("\n", parts);
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Returns the cached records for these files, or null when there is no usable cache.
    /// Any failure - missing, truncated, stale or unreadable - is treated as a miss, since
    /// re-parsing is always a correct fallback.
    /// </summary>
    public static IReadOnlyList<PlayRecord>? TryLoad(IEnumerable<string> filePaths)
    {
        try
        {
            if (!File.Exists(CacheFile))
                return null;

            using var stream = new FileStream(CacheFile, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.SequentialScan);
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            var magic = reader.ReadBytes(Magic.Length);
            if (!magic.AsSpan().SequenceEqual(Magic))
                return null;
            if (reader.ReadInt32() != FormatVersion)
                return null;
            if (!string.Equals(reader.ReadString(), BuildKey(filePaths), StringComparison.Ordinal))
                return null;

            int count = reader.ReadInt32();
            if (count is < 0 or > MaxRecords)
                return null;

            // Platform and country repeat constantly, so they are written once into a table
            // and referenced by index; this keeps the file small and restores the sharing.
            int poolSize = reader.ReadInt32();
            if (poolSize is < 0 or > MaxPoolEntries)
                return null;

            var pool = new string[poolSize];
            for (int i = 0; i < pool.Length; i++)
                pool[i] = reader.ReadString();

            var records = new List<PlayRecord>(count);
            for (int i = 0; i < count; i++)
            {
                records.Add(new PlayRecord
                {
                    TrackName = reader.ReadString(),
                    ArtistName = reader.ReadString(),
                    AlbumName = reader.ReadString(),
                    MsPlayed = reader.ReadInt32(),
                    Timestamp = ReadTimestamp(reader),
                    ReasonEnd = reader.ReadBoolean() ? reader.ReadString() : null,
                    Skipped = reader.ReadBoolean(),
                    Kind = ReadKind(reader),
                    ShowName = reader.ReadString(),
                    EpisodeName = reader.ReadString(),
                    Uri = reader.ReadString(),
                    Platform = pool[reader.ReadInt32()],
                    Country = pool[reader.ReadInt32()],
                    Shuffle = reader.ReadBoolean(),
                    Offline = reader.ReadBoolean(),
                    Incognito = reader.ReadBoolean(),
                    HasPlaybackFlags = reader.ReadBoolean(),
                });
            }
            return records;
        }
        // FormatException is what BinaryReader.ReadString throws on a mangled length prefix.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                     or EndOfStreamException or ArgumentException
                                     or IndexOutOfRangeException or OverflowException
                                     or FormatException or InvalidDataException)
        {
            // A corrupt or partially written cache must never break loading.
            return null;
        }
    }

    /// <summary>
    /// The parser hands out local-time timestamps, and DateTime.MinValue for rows with none. Ticks
    /// alone lose that Kind, which would make a cached load serialize dates differently
    /// (no UTC offset) from a fresh parse of the same export.
    /// </summary>
    private static DateTime ReadTimestamp(BinaryReader reader)
    {
        long ticks = reader.ReadInt64();
        return ticks == DateTime.MinValue.Ticks
            ? DateTime.MinValue
            : new DateTime(ticks, DateTimeKind.Local);
    }

    private static ContentKind ReadKind(BinaryReader reader)
    {
        var kind = (ContentKind)reader.ReadByte();
        return Enum.IsDefined(kind) ? kind : throw new InvalidDataException($"Unknown content kind {(int)kind}.");
    }

    /// <summary>
    /// Writes the cache for these files. Best-effort: a failure here costs nothing but the
    /// speed-up next time, so it is swallowed.
    /// </summary>
    public static void TrySave(IEnumerable<string> filePaths, IReadOnlyList<PlayRecord> records)
    {
        var temp = CacheFile + ".tmp";
        try
        {
            Directory.CreateDirectory(CacheDirectory);

            // Write to a temp file and move into place, so an interrupted save can't leave a
            // half-written cache behind.
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                       64 * 1024, FileOptions.SequentialScan))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(BuildKey(filePaths));
                writer.Write(records.Count);

                var indexOf = new Dictionary<string, int>(StringComparer.Ordinal);
                var pool = new List<string>();
                foreach (var r in records)
                {
                    Intern(r.Platform);
                    Intern(r.Country);
                }

                writer.Write(pool.Count);
                foreach (var value in pool)
                    writer.Write(value);

                foreach (var r in records)
                {
                    writer.Write(r.TrackName);
                    writer.Write(r.ArtistName);
                    writer.Write(r.AlbumName);
                    writer.Write(r.MsPlayed);
                    writer.Write(r.Timestamp.Ticks);
                    writer.Write(r.ReasonEnd is not null);
                    if (r.ReasonEnd is not null)
                        writer.Write(r.ReasonEnd);
                    writer.Write(r.Skipped);
                    writer.Write((byte)r.Kind);
                    writer.Write(r.ShowName);
                    writer.Write(r.EpisodeName);
                    writer.Write(r.Uri);
                    writer.Write(indexOf[r.Platform]);
                    writer.Write(indexOf[r.Country]);
                    writer.Write(r.Shuffle);
                    writer.Write(r.Offline);
                    writer.Write(r.Incognito);
                    writer.Write(r.HasPlaybackFlags);
                }

                void Intern(string value)
                {
                    if (indexOf.ContainsKey(value))
                        return;
                    indexOf[value] = pool.Count;
                    pool.Add(value);
                }
            }

            File.Move(temp, CacheFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
        }
    }

    /// <summary>Removes the cache file, e.g. after a format change or on user request.</summary>
    public static void Clear() => TryDelete(CacheFile);

    /// <summary>Size of the cache on disk in bytes, or zero when there is none.</summary>
    public static long SizeBytes()
    {
        try
        {
            var info = new FileInfo(CacheFile);
            return info.Exists ? info.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing useful to do; the cache is disposable by design.
        }
    }
}
