using System.IO;
using System.Text.Json;

namespace Sortify.Services;

/// <summary>
/// Small user-preferences blob persisted next to the record cache. Everything here is
/// convenience only, so any read or write failure degrades to defaults rather than
/// surfacing an error.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Folder passed to Open Folder last time, reopened at startup when it still exists.</summary>
    public string? LastFolder { get; set; }

    /// <summary>Whether to reload the last folder automatically on launch.</summary>
    public bool ReopenLastFolder { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsFile => Path.Combine(RecordCache.CacheDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), JsonOptions)
                   ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(RecordCache.CacheDirectory);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are best-effort.
        }
    }
}
