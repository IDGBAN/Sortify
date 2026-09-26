using System.IO;
using System.Text.Json;
using Sortify.Models;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

/// <summary>Covers the Markdown, JSON and shows-CSV exports.</summary>
public class ExportFormatsTests : IDisposable
{
    private readonly string _dir;

    public ExportFormatsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "SortifyTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    private static AnalysisResult SampleResult() => AnalysisEngine.Analyze(new[]
    {
        new PlayRecord
        {
            TrackName = "Pipe | Track", ArtistName = "Artist A", AlbumName = "Album A",
            MsPlayed = 180_000, Timestamp = new DateTime(2023, 1, 5, 12, 0, 0), ReasonEnd = "trackdone",
        },
        new PlayRecord
        {
            TrackName = "Second", ArtistName = "Artist B", AlbumName = "Album B",
            MsPlayed = 60_000, Timestamp = new DateTime(2023, 2, 7, 20, 0, 0), ReasonEnd = "fwdbtn",
        },
        new PlayRecord
        {
            Kind = ContentKind.Podcast, ShowName = "A Show", EpisodeName = "Ep 1",
            MsPlayed = 300_000, Timestamp = new DateTime(2023, 3, 1, 9, 0, 0),
        },
    }, new FilterOptions { MinMsPlayed = 0 });

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a|b", "a\\|b")]
    public void Md_EscapesTableSeparators(string input, string expected)
    {
        Assert.Equal(expected, ExportService.Md(input));
    }

    [Fact]
    public async Task Markdown_HasTheSummaryAndTheRankings()
    {
        var path = PathFor("report.md");

        await ExportService.SaveMarkdownAsync(path, SampleResult());
        var text = await File.ReadAllTextAsync(path);

        Assert.Contains("# Sortify report", text);
        Assert.Contains("## Summary", text);
        Assert.Contains("## Insights", text);
        Assert.Contains("## Top tracks", text);
        Assert.Contains("## Top artists", text);
        Assert.Contains("## Top albums", text);
        Assert.Contains("## By year", text);
        Assert.Contains("## Top shows and audiobooks", text);

        // The pipe in the track name must not split its table cell.
        Assert.Contains("Pipe \\| Track", text);
    }

    [Fact]
    public async Task Markdown_OmitsTheShowsTableWhenThereAreNone()
    {
        var path = PathFor("music-only.md");
        var result = AnalysisEngine.Analyze(new[]
        {
            new PlayRecord
            {
                TrackName = "Only", ArtistName = "Artist", AlbumName = "Album",
                MsPlayed = 60_000, Timestamp = new DateTime(2023, 1, 1, 12, 0, 0),
            },
        }, new FilterOptions { MinMsPlayed = 0 });

        await ExportService.SaveMarkdownAsync(path, result);
        var text = await File.ReadAllTextAsync(path);

        Assert.DoesNotContain("Top shows", text);
    }

    [Fact]
    public async Task Json_IsValidAndCarriesTheHeadlineNumbers()
    {
        var path = PathFor("results.json");
        var result = SampleResult();

        await ExportService.SaveJsonAsync(path, result);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var root = document.RootElement;

        Assert.Equal(result.TotalPlays, root.GetProperty("summary").GetProperty("totalPlays").GetInt32());
        Assert.Equal(result.UniqueTracks, root.GetProperty("summary").GetProperty("uniqueTracks").GetInt32());
        Assert.Equal(result.CompletedPlays, root.GetProperty("insights").GetProperty("completedPlays").GetInt32());
        Assert.Equal(result.Tracks.Count, root.GetProperty("tracks").GetArrayLength());
        Assert.Equal(24, root.GetProperty("hoursByHourOfDay").GetArrayLength());
        Assert.Equal(7, root.GetProperty("hoursByDayOfWeek").GetArrayLength());
        Assert.Equal(result.Shows.Count, root.GetProperty("shows").GetArrayLength());
    }

    [Fact]
    public async Task Json_LeavesNamesReadable()
    {
        var path = PathFor("unicode.json");
        var result = AnalysisEngine.Analyze(new[]
        {
            new PlayRecord
            {
                TrackName = "Café Ström", ArtistName = "Sigur Rós", AlbumName = "Ágætis",
                MsPlayed = 60_000, Timestamp = new DateTime(2023, 1, 1, 12, 0, 0),
            },
        }, new FilterOptions { MinMsPlayed = 0 });

        await ExportService.SaveJsonAsync(path, result);
        var text = await File.ReadAllTextAsync(path);

        // Escaped as é etc. the file would be unreadable to a human.
        Assert.Contains("Café Ström", text);
        Assert.Contains("Sigur Rós", text);
    }

    [Fact]
    public async Task ShowsCsv_HasAHeaderAndOneRowPerShow()
    {
        var path = PathFor("shows.csv");
        var result = SampleResult();

        await ExportService.SaveShowsCsvAsync(path, result);
        var lines = await File.ReadAllLinesAsync(path);

        Assert.StartsWith("Rank,Show,Kind,", lines[0]);
        Assert.Equal(result.Shows.Count + 1, lines.Length);
        Assert.Contains("A Show", lines[1]);
        Assert.Contains("Podcast", lines[1]);
    }

    [Fact]
    public async Task TxtSummary_IncludesTheNewInsights()
    {
        var path = PathFor("results.txt");

        await ExportService.SaveTxtAsync(path, SampleResult());
        var text = await File.ReadAllTextAsync(path);

        Assert.Contains("Completion rate:", text);
        Assert.Contains("Plays per track:", text);
    }
}
