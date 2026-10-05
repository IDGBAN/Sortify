using System.IO;
using System.Security.Cryptography;
using System.Text;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Xunit;

namespace Sortify.Tests;

/// <summary>Plays shown in the time zone of the country they were streamed from.</summary>
public class PlayTimeZonesTests
{
    private static readonly DateTime NoonUtc = new(2024, 7, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");

    [Fact]
    public void ThisPc_ConvertsTheWaySortifyAlwaysHas()
    {
        var local = PlayTimeZones.ThisPc.ToLocal(NoonUtc, "JP");

        Assert.Equal(NoonUtc.ToLocalTime(), local);
        Assert.Equal(DateTimeKind.Local, local.Kind);
        Assert.Equal(TimeZoneInfo.Local.Id, PlayTimeZones.ThisPc.CacheKey);
    }

    [Fact]
    public void FollowingTheCountry_UsesItsZone_SummerTimeIncluded()
    {
        var zones = PlayTimeZones.FollowCountry(TimeZoneInfo.Utc);

        Assert.Equal(new DateTime(2024, 7, 1, 21, 0, 0), zones.ToLocal(NoonUtc, "JP"));
        Assert.Equal(new DateTime(2024, 7, 1, 14, 0, 0), zones.ToLocal(NoonUtc, "DE"));
        Assert.Equal(new DateTime(2024, 1, 15, 13, 0, 0), zones.ToLocal(new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc), "de"));
        Assert.Equal(DateTimeKind.Unspecified, zones.ToLocal(NoonUtc, "JP").Kind);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("BR")]
    [InlineData("AU")]
    [InlineData("ZZ")]
    [InlineData("")]
    [InlineData(null)]
    public void SeveralZones_OrNoCountry_TakeTheFallback(string? country)
    {
        var zones = PlayTimeZones.FollowCountry(Tokyo);

        Assert.Equal(new DateTime(2024, 7, 1, 21, 0, 0), zones.ToLocal(NoonUtc, country));
    }

    [Fact]
    public void EveryCountryInTheTable_HasAZoneOnThisSystem()
    {
        var missing = PlayTimeZones.SingleZoneCountries
            .Where(kv => PlayTimeZones.Find(kv.Value) is null)
            .Select(kv => $"{kv.Key} {kv.Value}")
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void TheTable_LeavesOutCountriesWithSeveralZones()
    {
        foreach (var code in new[] { "US", "CA", "MX", "BR", "RU", "AU", "ID", "KZ", "MN", "CD", "GL", "KI", "FM", "PF" })
            Assert.False(PlayTimeZones.SingleZoneCountries.ContainsKey(code), code);
    }

    [Fact]
    public void TheCacheKey_SaysHowTimesWereConverted()
    {
        string utc = PlayTimeZones.FollowCountry(TimeZoneInfo.Utc).CacheKey;

        Assert.NotEqual(PlayTimeZones.FollowCountry(Tokyo).CacheKey, utc);
        Assert.NotEqual(PlayTimeZones.ThisPc.CacheKey, utc);
    }

    [Fact]
    public void TheSettings_PickTheConversion()
    {
        var settings = new AppSettings();
        Assert.False(PlayTimeZones.From(settings).FollowsCountry);

        settings.FollowCountryTimeZones = true;
        settings.FallbackTimeZoneId = "Tokyo Standard Time";
        var zones = PlayTimeZones.From(settings);
        Assert.True(zones.FollowsCountry);
        Assert.Equal(new DateTime(2024, 7, 1, 21, 0, 0), zones.ToLocal(NoonUtc, null));

        settings.FallbackTimeZoneId = "Nowhere Standard Time";
        settings.Normalize();
        Assert.Null(settings.FallbackTimeZoneId);
    }
}

/// <summary>Reading and caching an export with each play in its country's time zone.</summary>
[Collection(DataFolderCollection.Name)]
public class TimeZoneParsingTests : IDisposable
{
    // One play streamed from Japan, as the extended history records it with its country and
    // as the account-data export records it, to the minute and with no country.
    private const string Extended = """
        [
            { "ts": "2024-07-01T12:00:30Z", "ms_played": 200000, "conn_country": "JP",
              "master_metadata_track_name": "Song", "master_metadata_album_artist_name": "Band",
              "master_metadata_album_album_name": "LP" }
        ]
        """;

    private const string Legacy = """
        [
            { "endTime": "2024-07-01 12:00", "artistName": "Band", "trackName": "Song", "msPlayed": 200000 }
        ]
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SortifyZones_" + Guid.NewGuid().ToString("N"));

    public TimeZoneParsingTests()
    {
        Directory.CreateDirectory(_dir);
        RecordCache.Clear();
    }

    public void Dispose()
    {
        RecordCache.Clear();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task APlayInBothFormats_IsStillCountedOnce_WhenItsCountryMovesIt()
    {
        var files = new[] { Write("Streaming_History_Audio_2024.json", Extended), Write("StreamingHistory0.json", Legacy) };

        var parsed = await new HistoryParser(PlayTimeZones.FollowCountry(TimeZoneInfo.Utc)).ParseAsync(files);

        var play = Assert.Single(parsed.Records);
        Assert.Equal(1, parsed.DuplicatesRemoved);
        Assert.Equal(new DateTime(2024, 7, 1, 21, 0, 30), play.Timestamp);
    }

    [Fact]
    public async Task ByDefault_PlaysAreInThisPcsZone()
    {
        var file = Write("Streaming_History_Audio_2024.json", Extended);

        var play = Assert.Single((await new HistoryParser().ParseAsync(new[] { file })).Records);

        Assert.Equal(new DateTime(2024, 7, 1, 12, 0, 30, DateTimeKind.Utc).ToLocalTime(), play.Timestamp);
        Assert.Equal(DateTimeKind.Local, play.Timestamp.Kind);
    }

    [Fact]
    public void ThisPcsCacheKey_IsTheOneEarlierVersionsWrote()
    {
        var file = Write("Streaming_History_Audio_2024.json", Extended);
        var info = new FileInfo(file);
        string earlier = $"tz|{TimeZoneInfo.Local.Id}\n{info.FullName.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";

        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(earlier))), RecordCache.BuildKey(new[] { file }));
    }

    [Fact]
    public async Task ACacheIsOnlyUsed_ForTheSameConversion()
    {
        var files = new[] { Write("Streaming_History_Audio_2024.json", Extended) };
        var zones = PlayTimeZones.FollowCountry(TimeZoneInfo.Utc);
        var parsed = await new HistoryParser(zones).ParseAsync(files);

        RecordCache.TrySave(files, parsed.Records, zones);

        Assert.Null(RecordCache.TryLoad(files));
        var cached = Assert.Single(RecordCache.TryLoad(files, PlayTimeZones.FollowCountry(TimeZoneInfo.Utc))!);
        Assert.Equal(parsed.Records[0].Timestamp, cached.Timestamp);
        Assert.Equal(DateTimeKind.Unspecified, cached.Timestamp.Kind);
    }
}

/// <summary>Turning the setting on reads the open export again.</summary>
[Collection(WpfCollection.Name)]
public class TimeZoneSettingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SortifyZoneSetting_" + Guid.NewGuid().ToString("N"));

    public TimeZoneSettingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task ChangingTheSetting_ReadsTheExportAgain()
    {
        File.WriteAllText(Path.Combine(_dir, "Streaming_History_Audio_2024.json"), """
            [
                { "ts": "2024-07-01T12:00:00Z", "ms_played": 200000, "conn_country": "JP",
                  "master_metadata_track_name": "Song", "master_metadata_album_artist_name": "Band",
                  "master_metadata_album_album_name": "LP" }
            ]
            """);
        var settings = new AppSettings();

        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(settings);
            await vm.OpenFolderPathAsync(_dir);
            var before = await vm.BuildDetailAsync(DetailScope.Track, "Song", "Band");
            Assert.Equal(new DateTime(2024, 7, 1, 12, 0, 0, DateTimeKind.Utc).ToLocalTime(), before!.FirstPlayed);

            settings.FollowCountryTimeZones = true;
            settings.FallbackTimeZoneId = "UTC";
            await vm.ApplySettingsChangesAsync();

            var after = await vm.BuildDetailAsync(DetailScope.Track, "Song", "Band");
            Assert.Equal(new DateTime(2024, 7, 1, 21, 0, 0), after!.FirstPlayed);
        });
    }
}
