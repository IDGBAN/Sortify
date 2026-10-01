using System.Text.Json;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Xunit;

namespace Sortify.Tests;

/// <summary>Named filter sets, and the filters remembered between launches.</summary>
public class FilterPresetTests
{
    private static FilterViewModel Busy()
    {
        var vm = new FilterViewModel
        {
            MinSeconds = 30,
            IncludePodcasts = true,
            StartDate = new DateTime(2023, 1, 1),
            EndDate = new DateTime(2023, 12, 31),
            SearchTerm = "night",
            StartHour = 22,
            EndHour = 2,
        };
        vm.Days[0].IsSelected = false;
        vm.ExcludeArtist("Rain Sounds");
        vm.ExcludeTrack("Lullaby");
        return vm;
    }

    [Fact]
    public void Snapshot_ThenApply_PutsEveryFilterBack()
    {
        var saved = Busy().Snapshot("Nights");
        var vm = new FilterViewModel();

        vm.Apply(saved);

        Assert.True(vm.Snapshot().SameFiltersAs(saved));
        Assert.Equal(new[] { "Rain Sounds" }, vm.ExcludedArtists);
        Assert.False(vm.Days[0].IsSelected);
        Assert.Equal(22, vm.StartHour);
    }

    [Fact]
    public void Apply_RaisesASingleChange()
    {
        var vm = new FilterViewModel();
        int changes = 0;
        vm.FiltersChanged += (_, _) => changes++;

        vm.Apply(Busy().Snapshot());

        Assert.Equal(1, changes);
    }

    [Fact]
    public void PickingAPreset_AppliesIt()
    {
        var vm = new FilterViewModel();
        var preset = Busy().Snapshot("Nights");
        vm.LoadPresets(new[] { preset });

        vm.SelectedPreset = preset;

        Assert.Equal(30, vm.MinSeconds);
        Assert.Same(preset, vm.SelectedPreset);
    }

    [Fact]
    public void ChangingAFilterAfterwards_ClearsThePickedPreset()
    {
        var vm = new FilterViewModel();
        var preset = Busy().Snapshot("Nights");
        vm.LoadPresets(new[] { preset });
        vm.SelectedPreset = preset;

        vm.SearchTerm = "day";

        Assert.Null(vm.SelectedPreset);
        Assert.Single(vm.Presets);
    }

    [Fact]
    public void Save_AddsThePresetAndSelectsIt()
    {
        var vm = Busy();
        int persisted = 0;
        vm.PresetsChanged += (_, _) => persisted++;
        vm.NewPresetName = "  Nights  ";

        vm.SavePresetCommand.Execute(null);

        var preset = Assert.Single(vm.Presets);
        Assert.Equal("Nights", preset.Name);
        Assert.Same(preset, vm.SelectedPreset);
        Assert.Equal(string.Empty, vm.NewPresetName);
        Assert.Equal(1, persisted);
    }

    [Fact]
    public void Save_UnderATakenName_ReplacesIt()
    {
        var vm = Busy();
        vm.NewPresetName = "Nights";
        vm.SavePresetCommand.Execute(null);

        vm.MinSeconds = 90;
        vm.NewPresetName = "NIGHTS";
        vm.SavePresetCommand.Execute(null);

        var preset = Assert.Single(vm.Presets);
        Assert.Equal(90, preset.MinSeconds);
    }

    [Fact]
    public void Save_NeedsAName()
    {
        var vm = new FilterViewModel { NewPresetName = "   " };

        Assert.False(vm.SavePresetCommand.CanExecute(null));
    }

    [Fact]
    public void Save_StopsAtTheLimitAndSaysWhy()
    {
        var vm = new FilterViewModel();
        vm.LoadPresets(Enumerable.Range(0, AppSettings.MaxFilterPresets)
            .Select(i => new FilterPreset { Name = $"Set {i}" }));
        string? notice = null;
        vm.Notice += (_, message) => notice = message;

        vm.NewPresetName = "One more";
        vm.SavePresetCommand.Execute(null);

        Assert.Equal(AppSettings.MaxFilterPresets, vm.Presets.Count);
        Assert.Contains("Delete one", notice);
    }

    [Fact]
    public void Delete_ForgetsThePresetButKeepsTheFilters()
    {
        var vm = new FilterViewModel();
        var preset = Busy().Snapshot("Nights");
        vm.LoadPresets(new[] { preset });
        vm.SelectedPreset = preset;

        vm.DeletePresetCommand.Execute(null);

        Assert.Empty(vm.Presets);
        Assert.Null(vm.SelectedPreset);
        Assert.Equal(30, vm.MinSeconds);
        Assert.False(vm.DeletePresetCommand.CanExecute(null));
    }

    [Fact]
    public void Prompt_SaysWhetherThereIsAnythingToPick()
    {
        var vm = new FilterViewModel();
        Assert.Equal("Nothing saved yet", vm.PresetPrompt);

        vm.LoadPresets(new[] { new FilterPreset { Name = "Nights" } });

        Assert.Equal("Choose a saved set", vm.PresetPrompt);
        Assert.False(vm.HasSelectedPreset);
    }

    [Fact]
    public void RememberedSnapshot_KeepsOnlyWhatShouldOutliveTheSession()
    {
        var remembered = Busy().RememberedSnapshot();

        Assert.Equal(30, remembered.MinSeconds);
        Assert.True(remembered.IncludePodcasts);
        Assert.Equal(new[] { "Rain Sounds" }, remembered.ExcludedArtists);
        Assert.Equal(new[] { "Lullaby" }, remembered.ExcludedTracks);
        Assert.Null(remembered.StartDate);
        Assert.Equal(string.Empty, remembered.SearchTerm);
        Assert.Equal(0, remembered.StartHour);
        Assert.All(remembered.IncludedDays, Assert.True);
    }

    [Fact]
    public void Presets_SurviveTheSettingsFile()
    {
        var settings = new AppSettings
        {
            Filters = Busy().RememberedSnapshot(),
            FilterPresets = { Busy().Snapshot("Nights") },
        };

        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        restored.Normalize();

        Assert.True(restored.Filters!.SameFiltersAs(settings.Filters));
        Assert.True(restored.FilterPresets.Single().SameFiltersAs(settings.FilterPresets[0]));
        Assert.Equal("Nights", restored.FilterPresets[0].Name);
    }

    [Fact]
    public void Normalize_RepairsAHandEditedPresetList()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            { "FilterPresets": [
                null,
                { "Name": "  " },
                { "Name": "Nights", "StartHour": 40, "IncludedDays": [ true ], "ExcludedArtists": [ "A", null, " a " ] },
                { "Name": "nights" }
            ] }
            """)!;

        settings.Normalize();

        var preset = Assert.Single(settings.FilterPresets);
        Assert.Equal(23, preset.StartHour);
        Assert.Equal(7, preset.IncludedDays.Length);
        Assert.Equal(new[] { "A" }, preset.ExcludedArtists);
    }

}

/// <summary>
/// The main view model reaches ThemeService, whose palettes are pack URIs that only resolve
/// once a WPF Application exists, so these run on the shared WPF host.
/// </summary>
[Collection(WpfCollection.Name)]
public class RememberedFilterTests
{
    [Fact]
    public void RestoresTheRememberedFiltersOnLaunch()
    {
        WpfTestHost.Run(() =>
        {
            var remembered = new FilterPreset { MinSeconds = 30, ExcludedArtists = { "Rain Sounds" } };

            var vm = new MainViewModel(new AppSettings { Filters = remembered });

            Assert.Equal(new[] { "Rain Sounds" }, vm.Filters.ExcludedArtists);
            Assert.Equal(30, vm.Filters.MinSeconds);
            Assert.Contains("1 artist excluded", vm.ActiveFilters);
        });
    }

    [Fact]
    public void RemembersAnExclusionAsSoonAsItIsMade()
    {
        WpfTestHost.Run(() =>
        {
            var settings = new AppSettings();
            var vm = new MainViewModel(settings);

            vm.ExcludeArtistFromGrid("White Noise");

            Assert.Equal(new[] { "White Noise" }, settings.Filters!.ExcludedArtists);
        });
    }

    [Fact]
    public void DoesNotRememberADateRange()
    {
        WpfTestHost.Run(() =>
        {
            var settings = new AppSettings();
            var vm = new MainViewModel(settings);

            vm.Filters.StartDate = new DateTime(2023, 1, 1);

            Assert.Null(settings.Filters?.StartDate);
        });
    }

    [Fact]
    public void LoadsAndPersistsPresets()
    {
        WpfTestHost.Run(() =>
        {
            var settings = new AppSettings { FilterPresets = { new FilterPreset { Name = "Old" } } };
            var vm = new MainViewModel(settings);

            vm.Filters.NewPresetName = "New";
            vm.Filters.SavePresetCommand.Execute(null);

            Assert.Equal(new[] { "Old", "New" }, settings.FilterPresets.Select(p => p.Name));
        });
    }
}
