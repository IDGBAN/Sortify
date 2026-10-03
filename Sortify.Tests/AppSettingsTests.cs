using System.IO;
using System.Text.Json;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

public class AppSettingsTests
{
    [Fact]
    public void RememberFolder_PutsTheNewestFirstWithoutDuplicating()
    {
        var settings = new AppSettings();

        settings.RememberFolder(@"C:\one");
        settings.RememberFolder(@"C:\two");
        settings.RememberFolder(@"C:\one");

        Assert.Equal(new[] { @"C:\one", @"C:\two" }, settings.RecentFolders);
        Assert.Equal(@"C:\one", settings.LastFolder);
    }

    [Fact]
    public void RememberFolder_MatchesPathsCaseInsensitively()
    {
        var settings = new AppSettings();

        settings.RememberFolder(@"C:\Music\Export");
        settings.RememberFolder(@"c:\music\export");

        Assert.Single(settings.RecentFolders);
    }

    [Fact]
    public void RememberFolder_CapsTheList()
    {
        var settings = new AppSettings();

        for (int i = 0; i < AppSettings.MaxRecentFolders + 5; i++)
            settings.RememberFolder($@"C:\folder{i}");

        Assert.Equal(AppSettings.MaxRecentFolders, settings.RecentFolders.Count);
        // The most recent survives; the oldest are dropped.
        Assert.Equal($@"C:\folder{AppSettings.MaxRecentFolders + 4}", settings.RecentFolders[0]);
    }

    [Fact]
    public void RememberFolder_IgnoresBlankPaths()
    {
        var settings = new AppSettings();

        settings.RememberFolder("   ");

        Assert.Empty(settings.RecentFolders);
        Assert.Null(settings.LastFolder);
    }

    [Fact]
    public void PruneMissingFolders_DropsPathsThatAreGone()
    {
        var settings = new AppSettings();
        settings.RememberFolder(Path.GetTempPath());
        settings.RememberFolder(Path.Combine(Path.GetTempPath(), "sortify-does-not-exist"));

        settings.PruneMissingFolders();

        Assert.Single(settings.RecentFolders);
        Assert.Equal(Path.GetTempPath(), settings.RecentFolders[0]);
    }

    [Fact]
    public void PruneMissingFolders_KeepsAZipThatStillExists()
    {
        var zip = Path.Combine(Path.GetTempPath(), $"sortify-{Guid.NewGuid():N}.zip");
        File.WriteAllText(zip, "zip");
        try
        {
            var settings = new AppSettings();
            settings.RememberFolder(Path.Combine(Path.GetTempPath(), "sortify-gone.zip"));
            settings.RememberFolder(zip);

            settings.PruneMissingFolders();

            Assert.Equal(new[] { zip }, settings.RecentFolders);
            Assert.Equal(zip, settings.LastFolder);
        }
        finally
        {
            File.Delete(zip);
        }
    }

    [Fact]
    public void Normalize_CleansAHandEditedRecentList()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """{ "RecentFolders": [ "C:\\one", null, "   ", "c:\\ONE", "C:\\two" ] }""")!;

        settings.Normalize();

        Assert.Equal(new[] { @"C:\one", @"C:\two" }, settings.RecentFolders);
    }

    [Fact]
    public void SessionGap_IsClampedToASensibleRange()
    {
        Assert.Equal(TimeSpan.FromMinutes(AppSettings.MaxSessionGapMinutes),
            new AppSettings { SessionGapMinutes = 10_000 }.SessionGap);

        Assert.Equal(TimeSpan.FromMinutes(AppSettings.MinSessionGapMinutes),
            new AppSettings { SessionGapMinutes = -5 }.SessionGap);
    }

    [Fact]
    public void SessionGap_IsNotWrittenToTheSettingsFile()
    {
        // It is derived from SessionGapMinutes and has no setter, so persisting it would
        // put a value in the file that loading silently ignores.
        var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings());

        Assert.DoesNotContain("\"SessionGap\"", json);
        Assert.Contains("\"SessionGapMinutes\"", json);
    }

    [Fact]
    public void Save_RoundTripsWithoutLeavingTheTempFileBehind()
    {
        var settings = new AppSettings { SessionGapMinutes = 45 };
        settings.FilterPresets.Add(new Sortify.Models.FilterPreset { Name = "Gym" });

        settings.Save();
        var loaded = AppSettings.Load();

        Assert.Equal(45, loaded.SessionGapMinutes);
        Assert.Equal("Gym", Assert.Single(loaded.FilterPresets).Name);
        Assert.False(File.Exists(Path.Combine(AppPaths.DataDirectory, "settings.json.tmp")));
    }

    [Fact]
    public void SessionGap_DefaultsToThirtyMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), new AppSettings().SessionGap);
    }

    [Fact]
    public void DefaultSettings_CanBeSerialized()
    {
        // Regression: an unset window position was once double.NaN, which System.Text.Json
        // refuses to write - so the first save on a fresh install threw.
        var json = JsonSerializer.Serialize(new AppSettings());

        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Null(restored!.WindowLeft);
        Assert.Null(restored.WindowTop);
    }

    [Fact]
    public void WindowPosition_SurvivesARoundTrip()
    {
        var json = JsonSerializer.Serialize(new AppSettings
        {
            WindowLeft = 120.5,
            WindowTop = 64,
            WindowWidth = 1400,
            WindowHeight = 900,
        });

        var restored = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal(120.5, restored.WindowLeft);
        Assert.Equal(64, restored.WindowTop);
        Assert.Equal(1400, restored.WindowWidth);
    }
}
