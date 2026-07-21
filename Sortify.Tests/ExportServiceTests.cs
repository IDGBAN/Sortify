using System.IO;
using Sortify.Models;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

public class ExportServiceTests : IDisposable
{
    private readonly string _dir;

    public ExportServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "SortifyTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("has,comma", "\"has,comma\"")]
    [InlineData("has\"quote", "\"has\"\"quote\"")]
    [InlineData("has\nnewline", "\"has\nnewline\"")]
    [InlineData("has\rreturn", "\"has\rreturn\"")]
    public void Csv_QuotesOnlyWhenNeeded(string input, string expected)
    {
        Assert.Equal(expected, ExportService.Csv(input));
    }

    [Fact]
    public async Task TracksCsv_EscapesFieldsAndOmitsMissingDates()
    {
        var result = AnalysisEngine.Analyze(new[]
        {
            new PlayRecord
            {
                TrackName = "Hello, World",
                ArtistName = "The \"Band\"",
                MsPlayed = 60_000,
                Timestamp = DateTime.MinValue, // no timestamp
            },
        }, new FilterOptions { MinMsPlayed = 0 });

        var path = Path.Combine(_dir, "tracks.csv");
        await ExportService.SaveTracksCsvAsync(path, result);

        var lines = await File.ReadAllLinesAsync(path);
        Assert.Equal("Rank,Track,Artist,TotalTime,Hours,PlayCount,FirstPlayed,LastPlayed", lines[0]);
        Assert.Equal("1,\"Hello, World\",\"The \"\"Band\"\"\",00:01:00,0.02,1,,", lines[1]);
    }

    [Fact]
    public async Task Txt_ContainsSummaryAndRankings()
    {
        var result = AnalysisEngine.Analyze(new[]
        {
            new PlayRecord
            {
                TrackName = "Song",
                ArtistName = "Artist",
                AlbumName = "Album",
                MsPlayed = 3_600_000,
                Timestamp = new DateTime(2023, 5, 10, 14, 0, 0),
            },
        }, new FilterOptions { MinMsPlayed = 0 });

        var path = Path.Combine(_dir, "results.txt");
        await ExportService.SaveTxtAsync(path, result);

        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("Sortify Results", text);
        Assert.Contains("Total plays: 1", text);
        Assert.Contains("Tracks Ranked by Time Listened:", text);
        Assert.Contains("1. 01:00:00 - Song - Artist", text);
        Assert.Contains("Listening by Year:", text);
        Assert.Contains("2023:", text);
    }

    [Fact]
    public async Task YearsCsv_WritesOneRowPerYear()
    {
        var result = AnalysisEngine.Analyze(new[]
        {
            new PlayRecord { TrackName = "A", ArtistName = "X", MsPlayed = 60_000, Timestamp = new DateTime(2022, 1, 1, 12, 0, 0) },
            new PlayRecord { TrackName = "B", ArtistName = "Y", MsPlayed = 60_000, Timestamp = new DateTime(2023, 1, 1, 12, 0, 0) },
        }, new FilterOptions { MinMsPlayed = 0 });

        var path = Path.Combine(_dir, "years.csv");
        await ExportService.SaveYearsCsvAsync(path, result);

        var lines = await File.ReadAllLinesAsync(path);
        Assert.Equal(3, lines.Length); // header + 2 years
        Assert.StartsWith("2022,", lines[1]);
        Assert.StartsWith("2023,", lines[2]);
    }
}
