using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sortify.Models;

namespace Sortify.Services;

/// <summary>
/// User preferences persisted next to the record cache. Everything here is convenience
/// only, so any read or write failure degrades to defaults rather than surfacing an error.
/// </summary>
public sealed class AppSettings
{
    /// <summary>How many folders to remember in the "recent" list.</summary>
    public const int MaxRecentFolders = 8;

    /// <summary>Bounds on the break between plays that still counts as one session.</summary>
    public const int MinSessionGapMinutes = 1;
    public const int MaxSessionGapMinutes = 240;

    /// <summary>How many named filter sets can be saved.</summary>
    public const int MaxFilterPresets = 30;

    // ---- Data -------------------------------------------------------------------------------

    /// <summary>
    /// Export folder (or export ZIP) opened last time, reopened at startup when it still exists.
    /// </summary>
    public string? LastFolder { get; set; }

    /// <summary>Most recently opened export folders and ZIPs, newest first.</summary>
    public List<string> RecentFolders { get; set; } = new();

    /// <summary>Whether to reload the last folder automatically on launch.</summary>
    public bool ReopenLastFolder { get; set; } = true;

    /// <summary>Gap between plays that starts a new listening session, in minutes.</summary>
    public int SessionGapMinutes { get; set; } = 30;

    /// <summary>
    /// Shows each play in the time zone of the country it was streamed from rather than this
    /// PC's. Plays from a country with several zones, or with no country, use
    /// <see cref="FallbackTimeZoneId"/>.
    /// </summary>
    public bool FollowCountryTimeZones { get; set; }

    /// <summary>The zone for plays whose country doesn't settle it, by Windows id. Null for this PC's.</summary>
    public string? FallbackTimeZoneId { get; set; }

    /// <summary>
    /// The filters carried over to the next launch. Only the ones that describe what the
    /// user never wants counted (exclusions, the minimum duration, podcasts, private
    /// sessions, devices and countries left out) are kept here; a date range or a search
    /// coming back days later would just be confusing.
    /// </summary>
    public FilterPreset? Filters { get; set; }

    /// <summary>Named filter sets, in the order they were saved.</summary>
    public List<FilterPreset> FilterPresets { get; set; } = new();

    // ---- Appearance -------------------------------------------------------------------------

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>
    /// Chart entry animations. Worth turning off on very large histories, where animating
    /// thousands of bars costs more than it adds.
    /// </summary>
    public bool AnimateCharts { get; set; } = true;

    /// <summary>
    /// The day weeks start on: where weekly totals begin, and the order days are listed in.
    /// One of <see cref="Weeks.Starts"/>.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Sunday;

    /// <summary>Whether the charts and lists rank by listening time or by plays (the switch in the sidebar).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Metric Metric { get; set; } = Metric.Time;

    // ---- Window and layout -------------------------------------------------------------------

    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }

    // Nullable rather than NaN: System.Text.Json refuses to write NaN, which would have
    // made the very first save throw.
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    public bool WindowMaximized { get; set; }

    /// <summary>Whether the filter sidebar was expanded when the app last closed.</summary>
    public bool SidebarVisible { get; set; } = true;

    /// <summary>Tab index the user was last on.</summary>
    public int LastTabIndex { get; set; }

    // ---- Persistence -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsFile => Path.Combine(AppPaths.DataDirectory, "settings.json");

    /// <summary>Where a settings file that couldn't be read is moved, out of the way of the next save.</summary>
    internal static string UnreadableFile => Path.Combine(AppPaths.DataDirectory, "settings.unreadable.json");

    /// <summary>
    /// True when there was a settings file that couldn't be read, so these are the defaults and
    /// the file has been moved to <see cref="UnreadableFile"/>.
    /// </summary>
    [JsonIgnore]
    public bool WasUnreadable { get; internal set; }

    // Saves happen on nearly every change, so a folder that can't be written would fill the
    // log with the same failure.
    private static bool _saveFailureLogged;

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return new AppSettings();

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), JsonOptions)
                           ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (JsonException ex)
        {
            // The defaults would otherwise be saved over it, saved filter sets and all.
            ErrorLog.TryWrite(ex);
            return new AppSettings { WasUnreadable = TryMoveAside() };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.TryWrite(ex);
            return new AppSettings();
        }
    }

    private static bool TryMoveAside()
    {
        try
        {
            File.Move(SettingsFile, UnreadableFile, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.TryWrite(ex);
            return false;
        }
    }

    public void Save()
    {
        try
        {
            Normalize();
            Directory.CreateDirectory(AppPaths.DataDirectory);

            // Written aside and moved into place, like the record cache: a crash halfway
            // through a direct write would leave a file that loads as defaults, saved filter
            // sets and all.
            string temp = SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, SettingsFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                     or JsonException or ArgumentException or NotSupportedException)
        {
            // Preferences are best-effort: losing them costs the user a re-pick, whereas
            // throwing here would interrupt whatever they were actually doing.
            if (!_saveFailureLogged)
            {
                _saveFailureLogged = true;
                ErrorLog.TryWrite(ex);
            }
        }
    }

    /// <summary>
    /// Records a folder as the newest entry in the recent list. Also keeps
    /// <see cref="LastFolder"/> in step, since that is what startup restores.
    /// </summary>
    public void RememberFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return;

        RecentFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        RecentFolders.Insert(0, folder);
        if (RecentFolders.Count > MaxRecentFolders)
            RecentFolders.RemoveRange(MaxRecentFolders, RecentFolders.Count - MaxRecentFolders);

        LastFolder = folder;
    }

    /// <summary>Drops remembered folders and ZIPs that no longer exist on disk.</summary>
    public void PruneMissingFolders()
    {
        RecentFolders.RemoveAll(f => !Exists(f));
        if (LastFolder is not null && !Exists(LastFolder))
            LastFolder = null;
    }

    private static bool Exists(string path) =>
        ArchivePath.IsArchive(path) ? File.Exists(path) : Directory.Exists(path);

    /// <summary>
    /// <see cref="SessionGapMinutes"/> as a TimeSpan. Not persisted: it is derived, and
    /// writing it out puts a second, read-only copy of the same setting in the file.
    /// </summary>
    [JsonIgnore]
    public TimeSpan SessionGap => TimeSpan.FromMinutes(
        Math.Clamp(SessionGapMinutes, MinSessionGapMinutes, MaxSessionGapMinutes));

    /// <summary>
    /// Repairs values that an older settings file, a hand edit or a disconnected monitor
    /// could leave in an unusable state.
    /// </summary>
    internal void Normalize()
    {
        // A hand-edited file can hold nulls, blanks or the same folder twice; any of those
        // would show up as a broken entry in the Recent menu.
        RecentFolders = (RecentFolders ?? new List<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxRecentFolders)
            .ToList();
        SessionGapMinutes = Math.Clamp(SessionGapMinutes, MinSessionGapMinutes, MaxSessionGapMinutes);
        LastTabIndex = Math.Max(0, LastTabIndex);
        if (!Weeks.Starts.Contains(FirstDayOfWeek))
            FirstDayOfWeek = DayOfWeek.Sunday;
        if (PlayTimeZones.Find(FallbackTimeZoneId) is null)
            FallbackTimeZoneId = null;
        if (!Enum.IsDefined(Metric))
            Metric = Metric.Time;

        Filters?.Normalize();
        FilterPresets = (FilterPresets ?? new List<FilterPreset>())
            .Where(p => p is not null)
            .Select(p =>
            {
                p.Normalize();
                return p;
            })
            .Where(p => p.Name.Length > 0)
            .DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxFilterPresets)
            .ToList();

        // Settings written before recent folders existed only have LastFolder.
        if (RecentFolders.Count == 0 && !string.IsNullOrWhiteSpace(LastFolder))
            RecentFolders.Add(LastFolder);

        if (WindowWidth is < 600 or > 20000) WindowWidth = 0;
        if (WindowHeight is < 400 or > 20000) WindowHeight = 0;

        // A non-finite coordinate can't be written back out, and can't place a window either.
        if (WindowLeft is { } left && !double.IsFinite(left)) WindowLeft = null;
        if (WindowTop is { } top && !double.IsFinite(top)) WindowTop = null;
    }
}
