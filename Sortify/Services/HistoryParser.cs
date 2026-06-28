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
/// Reads selected Spotify extended streaming history JSON files into normalized PlayRecords.
/// Empty or invalid files are skipped gracefully (mirrors the original Python behavior).
/// </summary>
public sealed class HistoryParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<ParseResult> ParseAsync(
        IEnumerable<string> filePaths,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ParseResult();

        foreach (var path in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Reading {Path.GetFileName(path)}...");

            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0)
                {
                    result.SkippedFiles.Add(path);
                    continue;
                }

                await using var stream = File.OpenRead(path);
                var entries = await JsonSerializer
                    .DeserializeAsync<List<SpotifyHistoryEntry>>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                if (entries is null)
                {
                    result.SkippedFiles.Add(path);
                    continue;
                }

                foreach (var entry in entries)
                {
                    var record = Normalize(entry);
                    if (record is not null)
                        result.Records.Add(record);
                }
            }
            catch (JsonException ex)
            {
                result.Warnings.Add($"Skipping invalid JSON file: {Path.GetFileName(path)} ({ex.Message})");
            }
            catch (IOException ex)
            {
                result.Warnings.Add($"Could not read {Path.GetFileName(path)} ({ex.Message})");
            }
        }

        return result;
    }

    private static PlayRecord? Normalize(SpotifyHistoryEntry entry)
    {
        // Skip podcast / non-music rows that lack track metadata.
        if (string.IsNullOrWhiteSpace(entry.TrackName) && string.IsNullOrWhiteSpace(entry.ArtistName))
            return null;

        DateTime timestamp = DateTime.MinValue;
        if (!string.IsNullOrEmpty(entry.Ts) &&
            DateTime.TryParse(entry.Ts, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            timestamp = parsed;
        }

        return new PlayRecord
        {
            TrackName = string.IsNullOrWhiteSpace(entry.TrackName) ? "Unknown Track" : entry.TrackName!,
            ArtistName = string.IsNullOrWhiteSpace(entry.ArtistName) ? "Unknown Artist" : entry.ArtistName!,
            AlbumName = string.IsNullOrWhiteSpace(entry.AlbumName) ? "Unknown Album" : entry.AlbumName!,
            MsPlayed = entry.MsPlayed < 0 ? 0 : (int)Math.Min(entry.MsPlayed, int.MaxValue),
            Timestamp = timestamp,
            ReasonEnd = entry.ReasonEnd,
            Skipped = entry.Skipped ?? false,
        };
    }
}
