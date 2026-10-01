using System.Runtime.InteropServices;
using Sortify.Models;

namespace Sortify.Services;

/// <summary>
/// Merges the records of several history files so that a play exported twice is only
/// counted once. That happens when the account-data export and the extended history sit in
/// the same folder, or when two extended exports are loaded together.
/// </summary>
internal static class OverlapFilter
{
    /// <summary>One parsed file, and whether it was in the older account-data format.</summary>
    internal readonly record struct SourceFile(IReadOnlyList<PlayRecord> Records, bool IsLegacy);

    /// <summary>
    /// Returns the records of every file, in file order, minus the duplicates; the number
    /// left out comes back in <paramref name="removed"/>.
    /// </summary>
    public static List<PlayRecord> Merge(IReadOnlyList<SourceFile> files, out int removed)
    {
        int total = files.Sum(f => f.Records.Count);
        removed = 0;
        if (files.Count < 2)
            return files.SelectMany(f => f.Records).ToList();

        var merged = RemoveRepeatedFiles(files, total, ref removed);
        return RemoveLegacyCovered(merged, ref removed);
    }

    /// <summary>
    /// Two copies of the same export repeat every row exactly. A file can also repeat a row
    /// on its own (Spotify's exports occasionally do), so rather than keeping one of each,
    /// this keeps as many copies of a row as the single file holding the most of them. A
    /// lone export therefore comes through untouched.
    /// </summary>
    private static List<(PlayRecord Record, bool IsLegacy)> RemoveRepeatedFiles(
        IReadOnlyList<SourceFile> files, int total, ref int removed)
    {
        var counts = new Dictionary<ExactKey, Copies>(total);
        for (int i = 0; i < files.Count; i++)
        {
            foreach (var r in files[i].Records)
            {
                if (r.Timestamp == DateTime.MinValue)
                    continue;

                ref var c = ref CollectionsMarshal.GetValueRefOrAddDefault(counts, ExactKey.Of(r), out bool existed);
                if (!existed || c.File != i)
                {
                    c.File = i;
                    c.InFile = 0;
                }
                c.InFile++;
                c.Max = Math.Max(c.Max, c.InFile);
            }
        }

        var kept = new List<(PlayRecord, bool)>(total);
        foreach (var file in files)
        {
            foreach (var r in file.Records)
            {
                // Undated rows can't be matched against anything, so they always stay.
                if (r.Timestamp != DateTime.MinValue)
                {
                    ref var c = ref CollectionsMarshal.GetValueRefOrNullRef(counts, ExactKey.Of(r));
                    if (c.Kept >= c.Max)
                    {
                        removed++;
                        continue;
                    }
                    c.Kept++;
                }
                kept.Add((r, file.IsLegacy));
            }
        }
        return kept;
    }

    /// <summary>
    /// The account-data export covers the most recent year, so it mostly repeats plays the
    /// extended history already has. Its rows only carry an end time to the minute, and
    /// name the track's own artist where the extended history names the album artist, so
    /// a row is matched on the track name and the minute it ended. Whether that minute was
    /// rounded or cut short isn't documented, so the minute before counts too. Each
    /// extended play can only cover one account-data play.
    /// </summary>
    private static List<PlayRecord> RemoveLegacyCovered(List<(PlayRecord Record, bool IsLegacy)> rows, ref int removed)
    {
        var result = new List<PlayRecord>(rows.Count);
        if (!rows.Exists(r => r.IsLegacy) || !rows.Exists(r => !r.IsLegacy))
        {
            foreach (var (record, _) in rows)
                result.Add(record);
            return result;
        }

        var available = new Dictionary<MinuteKey, int>();
        foreach (var (r, isLegacy) in rows)
        {
            if (!isLegacy && r.Timestamp != DateTime.MinValue)
                CollectionsMarshal.GetValueRefOrAddDefault(available, MinuteKey.Of(r, 0), out _)++;
        }

        foreach (var (r, isLegacy) in rows)
        {
            if (isLegacy && r.Timestamp != DateTime.MinValue &&
                (TryTake(available, MinuteKey.Of(r, 0)) || TryTake(available, MinuteKey.Of(r, -1))))
            {
                removed++;
                continue;
            }
            result.Add(r);
        }
        return result;
    }

    private static bool TryTake(Dictionary<MinuteKey, int> available, MinuteKey key)
    {
        ref int count = ref CollectionsMarshal.GetValueRefOrNullRef(available, key);
        if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref count) || count == 0)
            return false;
        count--;
        return true;
    }

    private struct Copies
    {
        public int File;
        public int InFile;
        public int Max;
        public int Kept;
    }

    private readonly record struct ExactKey(
        DateTime Timestamp, int MsPlayed, ContentKind Kind, string Track, string Artist, string Album)
    {
        public static ExactKey Of(PlayRecord r) => r.Kind == ContentKind.Music
            ? new(r.Timestamp, r.MsPlayed, r.Kind, r.TrackName, r.ArtistName, r.AlbumName)
            : new(r.Timestamp, r.MsPlayed, r.Kind, r.EpisodeName, r.ShowName, string.Empty);
    }

    /// <summary>
    /// The account-data export files podcasts under the track and artist fields, so the
    /// name compared is the episode title for anything that isn't music.
    /// </summary>
    private readonly record struct MinuteKey(long Minute, string Name)
    {
        public static MinuteKey Of(PlayRecord r, int offsetMinutes)
        {
            long minute = r.Timestamp.Ticks / TimeSpan.TicksPerMinute + offsetMinutes;
            string name = r.Kind == ContentKind.Music ? r.TrackName : r.EpisodeName;
            return new MinuteKey(minute, name.ToUpperInvariant());
        }
    }
}
