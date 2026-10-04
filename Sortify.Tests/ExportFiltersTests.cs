using System.IO;
using System.Text.Json;
using Sortify.Models;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

/// <summary>Reports say which filters they were made under.</summary>
public class ExportFiltersTests : IDisposable
{
    private static readonly string[] Filters = { "2023-01-01 to 2023-12-31", "Sun, Sat", "2 artists excluded" };

    private readonly string _dir;

    public ExportFiltersTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "SortifyTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static AnalysisResult Result() => AnalysisEngine.Analyze(new[]
    {
        new PlayRecord
        {
            TrackName = "Song", ArtistName = "Artist", AlbumName = "Album",
            MsPlayed = 180_000, Timestamp = new DateTime(2023, 1, 5, 12, 0, 0),
        },
    }, new FilterOptions());

    [Fact]
    public async Task Text_ListsTheFiltersUnderTheHeading()
    {
        var path = Path.Combine(_dir, "results.txt");

        await ExportService.SaveTxtAsync(path, Result(), Filters);
        var lines = await File.ReadAllLinesAsync(path);

        Assert.Equal("Filters: 2023-01-01 to 2023-12-31; Sun, Sat; 2 artists excluded", lines[2]);
    }

    [Fact]
    public async Task Text_SaysWhenNothingWasFiltered()
    {
        var path = Path.Combine(_dir, "results.txt");

        await ExportService.SaveTxtAsync(path, Result());

        Assert.Contains("Filters: none (every play in the export)", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Markdown_ListsTheFiltersWithFormattingEscaped()
    {
        var path = Path.Combine(_dir, "report.md");

        await ExportService.SaveMarkdownAsync(path, Result(), new[] { "Search “*hits*”" });
        var text = await File.ReadAllTextAsync(path);

        Assert.Contains("Filters: Search “\\*hits\\*”", text);
    }

    [Fact]
    public async Task Json_CarriesTheFiltersAsAnArray()
    {
        var path = Path.Combine(_dir, "results.json");

        await ExportService.SaveJsonAsync(path, Result(), Filters);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));

        Assert.Equal(Filters, doc.RootElement.GetProperty("filters").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Json_HasAnEmptyArrayWhenNothingWasFiltered()
    {
        var path = Path.Combine(_dir, "results.json");

        await ExportService.SaveJsonAsync(path, Result());
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));

        Assert.Equal(0, doc.RootElement.GetProperty("filters").GetArrayLength());
    }
}
