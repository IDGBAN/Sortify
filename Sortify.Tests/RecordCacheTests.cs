using System.IO;
using Sortify.Models;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

/// <summary>
/// The cache writes to a single per-user file, so these tests share it and must not run in
/// parallel with each other.
/// </summary>
[Collection(DataFolderCollection.Name)]
public class RecordCacheTests : IDisposable
{
    private readonly string _dir;

    public RecordCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "SortifyCache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        RecordCache.Clear();
    }

    public void Dispose()
    {
        RecordCache.Clear();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string WriteFile(string name, string content = "[]")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static PlayRecord Music(string track = "T", string artist = "A") => new()
    {
        TrackName = track,
        ArtistName = artist,
        AlbumName = "Al",
        MsPlayed = 123_456,
        Timestamp = new DateTime(2023, 5, 10, 14, 30, 0),
        ReasonEnd = "trackdone",
        Skipped = false,
        Kind = ContentKind.Music,
        Uri = "spotify:track:abc",
        Platform = "windows 10",
        Country = "CA",
        Shuffle = true,
        Offline = false,
        Incognito = false,
        HasPlaybackFlags = true,
    };

    [Fact]
    public void Miss_WhenNothingCached()
    {
        var file = WriteFile("a.json");
        Assert.Null(RecordCache.TryLoad(new[] { file }));
    }

    [Fact]
    public void RoundTrips_AllRecordFields()
    {
        var file = WriteFile("a.json");
        var original = Music();

        RecordCache.TrySave(new[] { file }, new[] { original });
        var loaded = RecordCache.TryLoad(new[] { file });

        var r = Assert.Single(loaded!);
        Assert.Equal(original.TrackName, r.TrackName);
        Assert.Equal(original.ArtistName, r.ArtistName);
        Assert.Equal(original.AlbumName, r.AlbumName);
        Assert.Equal(original.MsPlayed, r.MsPlayed);
        Assert.Equal(original.Timestamp, r.Timestamp);
        Assert.Equal(original.ReasonEnd, r.ReasonEnd);
        Assert.Equal(original.Skipped, r.Skipped);
        Assert.Equal(original.Kind, r.Kind);
        Assert.Equal(original.Uri, r.Uri);
        Assert.Equal(original.Platform, r.Platform);
        Assert.Equal(original.Country, r.Country);
        Assert.Equal(original.Shuffle, r.Shuffle);
        Assert.Equal(original.Offline, r.Offline);
        Assert.Equal(original.Incognito, r.Incognito);
        Assert.Equal(original.HasPlaybackFlags, r.HasPlaybackFlags);
    }

    [Fact]
    public void RoundTrips_PodcastRecords()
    {
        var file = WriteFile("a.json");
        var original = new PlayRecord
        {
            Kind = ContentKind.Audiobook,
            ShowName = "A Long Book",
            EpisodeName = "Chapter 3",
            MsPlayed = 900_000,
            Timestamp = new DateTime(2023, 1, 1),
        };

        RecordCache.TrySave(new[] { file }, new[] { original });

        var r = Assert.Single(RecordCache.TryLoad(new[] { file })!);
        Assert.Equal(ContentKind.Audiobook, r.Kind);
        Assert.Equal("A Long Book", r.ShowName);
        Assert.Equal("Chapter 3", r.EpisodeName);
    }

    [Fact]
    public void RoundTrips_NullReasonEnd()
    {
        var file = WriteFile("a.json");
        var original = new PlayRecord { TrackName = "T", ArtistName = "A", ReasonEnd = null };

        RecordCache.TrySave(new[] { file }, new[] { original });

        Assert.Null(Assert.Single(RecordCache.TryLoad(new[] { file })!).ReasonEnd);
    }

    [Fact]
    public void RestoresStringSharing_ForPlatformAndCountry()
    {
        var file = WriteFile("a.json");
        var records = new[] { Music("A"), Music("B") };

        RecordCache.TrySave(new[] { file }, records);
        var loaded = RecordCache.TryLoad(new[] { file })!;

        Assert.Same(loaded[0].Platform, loaded[1].Platform);
        Assert.Same(loaded[0].Country, loaded[1].Country);
    }

    [Fact]
    public void Invalidated_WhenAFileChanges()
    {
        var file = WriteFile("a.json");
        RecordCache.TrySave(new[] { file }, new[] { Music() });
        Assert.NotNull(RecordCache.TryLoad(new[] { file }));

        // Rewriting the file changes its length and write time.
        File.WriteAllText(file, "[{\"ms_played\": 1}]");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));

        Assert.Null(RecordCache.TryLoad(new[] { file }));
    }

    [Fact]
    public void Invalidated_WhenAFileIsAdded()
    {
        var first = WriteFile("a.json");
        RecordCache.TrySave(new[] { first }, new[] { Music() });

        var second = WriteFile("b.json");

        Assert.Null(RecordCache.TryLoad(new[] { first, second }));
    }

    [Fact]
    public void Invalidated_WhenAFileIsRemoved()
    {
        var first = WriteFile("a.json");
        var second = WriteFile("b.json");
        RecordCache.TrySave(new[] { first, second }, new[] { Music() });

        Assert.Null(RecordCache.TryLoad(new[] { first }));
    }

    [Fact]
    public void Key_IsIndependentOfFileOrder()
    {
        var first = WriteFile("a.json");
        var second = WriteFile("b.json");

        Assert.Equal(
            RecordCache.BuildKey(new[] { first, second }),
            RecordCache.BuildKey(new[] { second, first }));
    }

    [Fact]
    public void CorruptCache_IsTreatedAsAMiss()
    {
        var file = WriteFile("a.json");
        RecordCache.TrySave(new[] { file }, new[] { Music() });

        var cachePath = Path.Combine(AppPaths.DataDirectory, "records.cache");
        var bytes = File.ReadAllBytes(cachePath);
        File.WriteAllBytes(cachePath, bytes[..(bytes.Length / 2)]);

        Assert.Null(RecordCache.TryLoad(new[] { file }));
    }

    [Fact]
    public void GarbageCache_IsTreatedAsAMiss()
    {
        var file = WriteFile("a.json");
        Directory.CreateDirectory(AppPaths.DataDirectory);
        File.WriteAllText(Path.Combine(AppPaths.DataDirectory, "records.cache"), "not a cache file");

        Assert.Null(RecordCache.TryLoad(new[] { file }));
    }

    [Fact]
    public void AnImplausibleRecordCount_IsTreatedAsAMiss()
    {
        var file = WriteFile("a.json");
        RecordCache.TrySave(new[] { file }, new[] { Music() });

        // Header layout: magic, format version, then the length-prefixed key, then the
        // record count. Overwriting the count is what a truncated write or a bit flip does,
        // and the reader must not try to allocate a list that size.
        var cachePath = Path.Combine(AppPaths.DataDirectory, "records.cache");
        var bytes = File.ReadAllBytes(cachePath);
        int keyLength = RecordCache.BuildKey(new[] { file }).Length;
        int countOffset = 6 + sizeof(int) + 1 + keyLength;

        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, countOffset);
        File.WriteAllBytes(cachePath, bytes);

        Assert.Null(RecordCache.TryLoad(new[] { file }));
    }

    [Fact]
    public void ANegativeStringCount_IsTreatedAsAMiss()
    {
        var file = WriteFile("a.json");
        RecordCache.TrySave(new[] { file }, new[] { Music() });

        var cachePath = Path.Combine(AppPaths.DataDirectory, "records.cache");
        var bytes = File.ReadAllBytes(cachePath);
        int keyLength = RecordCache.BuildKey(new[] { file }).Length;
        int stringCountOffset = 6 + sizeof(int) + 1 + keyLength + sizeof(int);

        BitConverter.GetBytes(-5).CopyTo(bytes, stringCountOffset);
        File.WriteAllBytes(cachePath, bytes);

        Assert.Null(RecordCache.TryLoad(new[] { file }));
    }

    [Fact]
    public void RoundTrips_TimestampsAsLocalTime()
    {
        // The parser hands out local-kind timestamps. Losing the kind made a cached load
        // export dates without their UTC offset, unlike a fresh parse of the same files.
        var file = WriteFile("a.json");
        var dated = new PlayRecord { TrackName = "T", Timestamp = new DateTime(2023, 5, 10, 14, 30, 0, DateTimeKind.Local) };
        var undated = new PlayRecord { TrackName = "U", Timestamp = DateTime.MinValue };

        RecordCache.TrySave(new[] { file }, new[] { dated, undated });
        var loaded = RecordCache.TryLoad(new[] { file })!;

        Assert.Equal(DateTimeKind.Local, loaded[0].Timestamp.Kind);
        Assert.Equal(dated.Timestamp, loaded[0].Timestamp);
        Assert.Equal(DateTime.MinValue, loaded[1].Timestamp);
    }

    /// <summary>Where the string table's strings start, found by the first three of them rather than by offset arithmetic.</summary>
    private static int StringTableOffset(byte[] bytes)
    {
        byte[] marker = { 1, (byte)'T', 1, (byte)'A', 2, (byte)'A', (byte)'l' };
        int index = bytes.AsSpan().IndexOf(marker);
        Assert.True(index >= 0, "string table not found in the cache file");
        return index;
    }

    /// <summary>
    /// Where the first record of a cache of <see cref="Music"/> starts: after the last string in
    /// the table, at the indexes of its track, artist and album, which are the first three strings.
    /// </summary>
    private static int FirstRecordOffset(byte[] bytes)
    {
        byte[] marker = { 2, (byte)'C', (byte)'A', 0, 1, 2 };
        int index = bytes.AsSpan().IndexOf(marker);
        Assert.True(index >= 0, "record marker not found in the cache file");
        return index + 3;
    }

    [Fact]
    public void AMangledStringLength_IsTreatedAsAMiss()
    {
        var file = WriteFile("a.json");
        RecordCache.TrySave(new[] { file }, new[] { Music() });

        // Five continuation bytes are not a valid 7-bit length, which BinaryReader reports
        // with a FormatException rather than an IOException.
        var cachePath = Path.Combine(AppPaths.DataDirectory, "records.cache");
        var bytes = File.ReadAllBytes(cachePath);
        new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }.CopyTo(bytes, StringTableOffset(bytes));
        File.WriteAllBytes(cachePath, bytes);

        Assert.Null(RecordCache.TryLoad(new[] { file }));
    }

    [Fact]
    public void AnUnknownContentKind_IsTreatedAsAMiss()
    {
        var file = WriteFile("a.json");
        RecordCache.TrySave(new[] { file }, new[] { Music() });

        // The track, artist and album indexes, ms played, ticks, the reason's index, then the
        // skipped flag; the kind byte follows.
        var cachePath = Path.Combine(AppPaths.DataDirectory, "records.cache");
        var bytes = File.ReadAllBytes(cachePath);
        int kindOffset = FirstRecordOffset(bytes) + 3 + sizeof(int) + sizeof(long) + 1 + 1;
        Assert.Equal((byte)ContentKind.Music, bytes[kindOffset]);
        bytes[kindOffset] = 200;
        File.WriteAllBytes(cachePath, bytes);

        Assert.Null(RecordCache.TryLoad(new[] { file }));
    }

    [Fact]
    public void AStringIndexPastTheTable_IsTreatedAsAMiss()
    {
        var file = WriteFile("a.json");
        RecordCache.TrySave(new[] { file }, new[] { Music() });

        var cachePath = Path.Combine(AppPaths.DataDirectory, "records.cache");
        var bytes = File.ReadAllBytes(cachePath);
        bytes[FirstRecordOffset(bytes)] = 100;
        File.WriteAllBytes(cachePath, bytes);

        Assert.Null(RecordCache.TryLoad(new[] { file }));
    }

    [Fact]
    public void LoadedPlays_ShareOneCopyOfEachName()
    {
        var file = WriteFile("a.json");
        RecordCache.TrySave(new[] { file }, new[] { Music(), Music() });

        var loaded = RecordCache.TryLoad(new[] { file })!;

        Assert.Same(loaded[0].TrackName, loaded[1].TrackName);
        Assert.Same(loaded[0].ArtistName, loaded[1].ArtistName);
        Assert.Same(loaded[0].AlbumName, loaded[1].AlbumName);
        Assert.Same(loaded[0].Uri, loaded[1].Uri);
    }

    [Fact]
    public void Clear_RemovesTheCache()
    {
        var file = WriteFile("a.json");
        RecordCache.TrySave(new[] { file }, new[] { Music() });
        Assert.NotNull(RecordCache.TryLoad(new[] { file }));

        RecordCache.Clear();

        Assert.Null(RecordCache.TryLoad(new[] { file }));
    }
}
