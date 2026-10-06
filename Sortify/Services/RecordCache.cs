using System.IO;
using System.Text;
using Sortify.Models;

namespace Sortify.Services;

/// <summary>A cached history: the plays, what each file held, and how many plays two files shared.</summary>
public sealed record CachedHistory(IReadOnlyList<PlayRecord> Records, IReadOnlyList<LoadedFile> Files, int DuplicatesRemoved);

/// <summary>
/// Caches parsed <see cref="PlayRecord"/>s in a compact binary file so reopening the same
/// export skips JSON parsing entirely. The cache is keyed by the exact set of source files
/// and their sizes and write times, so editing, adding or removing a file invalidates it.
/// </summary>
public static class RecordCache
{
    /// <summary>
    /// Bumped whenever the record layout or what the parser reads out of an export changes,
    /// so stale caches are ignored.
    /// </summary>
    private const int FormatVersion = 4;

    /// <summary>
    /// Upper bounds on the counts read out of the file, so a truncated or corrupt cache asks
    /// for a plausible allocation rather than an enormous one. All are far past any real
    /// export: Spotify's own limit is a few hundred thousand plays, and most plays bring no
    /// string the ones before them didn't already have.
    /// </summary>
    private const int MaxRecords = 50_000_000;
    private const int MaxStrings = 10_000_000;
    private const int MaxFiles = 100_000;

    private static readonly byte[] Magic = "SRTFY\0"u8.ToArray();

    private static string CacheFile => Path.Combine(AppPaths.DataDirectory, "records.cache");

    /// <summary>
    /// Fingerprints the source files. Two runs over an unchanged export produce the same
    /// key; any change to the file set, a file's length or its write time produces a
    /// different one. How times were converted is part of it too (the local time zone, or the
    /// country setting), because the cached timestamps were already converted to local time
    /// when the export was parsed. A file inside a ZIP is fingerprinted by the archive it came
    /// from plus its name within it.
    /// </summary>
    internal static string BuildKey(IEnumerable<string> filePaths, PlayTimeZones? timeZones = null)
    {
        var parts = filePaths
            .Select(p =>
            {
                string file = p, entry = string.Empty;
                if (ArchivePath.TrySplit(p, out var archive, out var entryName))
                {
                    file = archive;
                    entry = "|" + entryName;
                }

                var info = new FileInfo(file);
                long length = info.Exists ? info.Length : -1;
                long ticks = info.Exists ? info.LastWriteTimeUtc.Ticks : -1;
                return $"{info.FullName.ToLowerInvariant()}|{length}|{ticks}{entry}";
            })
            .OrderBy(s => s, StringComparer.Ordinal)
            .Prepend($"tz|{(timeZones ?? PlayTimeZones.ThisPc).CacheKey}");

        var joined = string.Join("\n", parts);
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Returns the cached records for these files, or null when there is no usable cache.
    /// Any failure - missing, truncated, stale or unreadable - is treated as a miss, since
    /// re-parsing is always a correct fallback.
    /// </summary>
    public static IReadOnlyList<PlayRecord>? TryLoad(IEnumerable<string> filePaths, PlayTimeZones? timeZones = null) =>
        TryLoadHistory(filePaths, timeZones)?.Records;

    /// <summary>
    /// The cached plays along with what each file held, or null when there is no usable cache.
    /// </summary>
    public static CachedHistory? TryLoadHistory(IEnumerable<string> filePaths, PlayTimeZones? timeZones = null)
    {
        timeZones ??= PlayTimeZones.ThisPc;
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
            if (!string.Equals(reader.ReadString(), BuildKey(filePaths, timeZones), StringComparison.Ordinal))
                return null;

            int count = reader.ReadInt32();
            if (count is < 0 or > MaxRecords)
                return null;

            // Names, links, devices and countries repeat across thousands of plays, so each
            // distinct one is written once into a table that the plays point into. That keeps
            // the file small, and every play of a track shares one copy of its names again.
            int stringCount = reader.ReadInt32();
            if (stringCount is < 0 or > MaxStrings)
                return null;

            var strings = new string[stringCount];
            for (int i = 0; i < strings.Length; i++)
                strings[i] = reader.ReadString();

            var records = new List<PlayRecord>(count);
            for (int i = 0; i < count; i++)
            {
                records.Add(new PlayRecord
                {
                    TrackName = strings[reader.Read7BitEncodedInt()],
                    ArtistName = strings[reader.Read7BitEncodedInt()],
                    AlbumName = strings[reader.Read7BitEncodedInt()],
                    MsPlayed = reader.ReadInt32(),
                    Timestamp = ReadTimestamp(reader, timeZones.Kind),
                    ReasonEnd = ReadOptional(reader, strings),
                    Skipped = reader.ReadBoolean(),
                    Kind = ReadKind(reader),
                    ShowName = strings[reader.Read7BitEncodedInt()],
                    EpisodeName = strings[reader.Read7BitEncodedInt()],
                    Uri = strings[reader.Read7BitEncodedInt()],
                    Platform = strings[reader.Read7BitEncodedInt()],
                    Country = strings[reader.Read7BitEncodedInt()],
                    Shuffle = reader.ReadBoolean(),
                    Offline = reader.ReadBoolean(),
                    Incognito = reader.ReadBoolean(),
                    HasPlaybackFlags = reader.ReadBoolean(),
                });
            }

            int fileCount = reader.ReadInt32();
            if (fileCount is < 0 or > MaxFiles)
                return null;

            var files = new List<LoadedFile>(fileCount);
            for (int i = 0; i < fileCount; i++)
            {
                string path = reader.ReadString();
                var format = (LoadedFileFormat)reader.ReadByte();
                if (!Enum.IsDefined(format))
                    throw new InvalidDataException($"Unknown file format {(int)format}.");
                int plays = reader.ReadInt32();
                var first = ReadTimestamp(reader, timeZones.Kind);
                var last = ReadTimestamp(reader, timeZones.Kind);
                string? problem = reader.ReadBoolean() ? reader.ReadString() : null;
                files.Add(new LoadedFile(path, HistoryParser.DisplayName(path), format, plays,
                    first == DateTime.MinValue ? null : first, last == DateTime.MinValue ? null : last, problem));
            }

            return new CachedHistory(records, files, reader.ReadInt32());
        }
        // FormatException is what BinaryReader.ReadString throws on a mangled length prefix.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                     or EndOfStreamException or ArgumentException
                                     or IndexOutOfRangeException or OverflowException
                                     or FormatException or InvalidDataException)
        {
            // A corrupt or partially written cache must never break loading.
            ErrorLog.TryWrite(ex);
            return null;
        }
    }

    /// <summary>
    /// The parser hands out local-time timestamps, of the kind <see cref="PlayTimeZones.Kind"/>
    /// says, and DateTime.MinValue for rows with none. Ticks alone lose that Kind, which would
    /// make a cached load serialize dates differently from a fresh parse of the same export.
    /// </summary>
    private static DateTime ReadTimestamp(BinaryReader reader, DateTimeKind kind)
    {
        long ticks = reader.ReadInt64();
        return ticks == DateTime.MinValue.Ticks
            ? DateTime.MinValue
            : new DateTime(ticks, kind);
    }

    /// <summary>A string that may be missing, written as its index plus one, or zero for none.</summary>
    private static string? ReadOptional(BinaryReader reader, string[] strings)
    {
        int index = reader.Read7BitEncodedInt();
        return index == 0 ? null : strings[index - 1];
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
    public static void TrySave(
        IEnumerable<string> filePaths, IReadOnlyList<PlayRecord> records, PlayTimeZones? timeZones = null,
        IReadOnlyList<LoadedFile>? files = null, int duplicatesRemoved = 0)
    {
        files ??= Array.Empty<LoadedFile>();
        var temp = CacheFile + ".tmp";
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);

            // Write to a temp file and move into place, so an interrupted save can't leave a
            // half-written cache behind.
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                       64 * 1024, FileOptions.SequentialScan))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(BuildKey(filePaths, timeZones));
                writer.Write(records.Count);

                var indexOf = new Dictionary<string, int>(StringComparer.Ordinal);
                var strings = new List<string>();
                foreach (var r in records)
                {
                    Intern(r.TrackName);
                    Intern(r.ArtistName);
                    Intern(r.AlbumName);
                    if (r.ReasonEnd is not null)
                        Intern(r.ReasonEnd);
                    Intern(r.ShowName);
                    Intern(r.EpisodeName);
                    Intern(r.Uri);
                    Intern(r.Platform);
                    Intern(r.Country);
                }

                writer.Write(strings.Count);
                foreach (var value in strings)
                    writer.Write(value);

                foreach (var r in records)
                {
                    writer.Write7BitEncodedInt(indexOf[r.TrackName]);
                    writer.Write7BitEncodedInt(indexOf[r.ArtistName]);
                    writer.Write7BitEncodedInt(indexOf[r.AlbumName]);
                    writer.Write(r.MsPlayed);
                    writer.Write(r.Timestamp.Ticks);
                    writer.Write7BitEncodedInt(r.ReasonEnd is null ? 0 : indexOf[r.ReasonEnd] + 1);
                    writer.Write(r.Skipped);
                    writer.Write((byte)r.Kind);
                    writer.Write7BitEncodedInt(indexOf[r.ShowName]);
                    writer.Write7BitEncodedInt(indexOf[r.EpisodeName]);
                    writer.Write7BitEncodedInt(indexOf[r.Uri]);
                    writer.Write7BitEncodedInt(indexOf[r.Platform]);
                    writer.Write7BitEncodedInt(indexOf[r.Country]);
                    writer.Write(r.Shuffle);
                    writer.Write(r.Offline);
                    writer.Write(r.Incognito);
                    writer.Write(r.HasPlaybackFlags);
                }

                writer.Write(files.Count);
                foreach (var f in files)
                {
                    writer.Write(f.Path);
                    writer.Write((byte)f.Format);
                    writer.Write(f.Plays);
                    writer.Write((f.First ?? DateTime.MinValue).Ticks);
                    writer.Write((f.Last ?? DateTime.MinValue).Ticks);
                    writer.Write(f.Problem is not null);
                    if (f.Problem is not null)
                        writer.Write(f.Problem);
                }
                writer.Write(duplicatesRemoved);

                void Intern(string value)
                {
                    if (indexOf.TryAdd(value, strings.Count))
                        strings.Add(value);
                }
            }

            File.Move(temp, CacheFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.TryWrite(ex);
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
