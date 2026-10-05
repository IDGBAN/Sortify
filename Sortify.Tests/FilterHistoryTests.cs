using System.Windows.Input;
using Sortify.Services;
using Sortify.ViewModels;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>Back and forward through filter changes.</summary>
public class FilterHistoryTests
{
    /// <summary>A sidebar with its starting filters already recorded, as after a history loads.</summary>
    private static FilterViewModel Recorded()
    {
        var vm = new FilterViewModel();
        vm.CommitHistory();
        return vm;
    }

    [Fact]
    public void NothingToUndo_BeforeTheFirstChange()
    {
        var vm = Recorded();

        Assert.False(vm.CanUndo);
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.False(vm.RedoCommand.CanExecute(null));
    }

    [Fact]
    public void Undo_PutsBackTheFiltersFromBeforeTheLastChange()
    {
        var vm = Recorded();
        vm.SearchTerm = "rain";
        vm.CommitHistory();
        vm.SetRange(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));
        vm.CommitHistory();

        vm.UndoCommand.Execute(null);

        Assert.Equal("rain", vm.SearchTerm);
        Assert.Null(vm.StartDate);
        Assert.Null(vm.EndDate);
        Assert.True(vm.CanUndo);
        Assert.True(vm.CanRedo);
    }

    [Fact]
    public void Redo_PutsTheChangeBack()
    {
        var vm = Recorded();
        vm.SearchTerm = "rain";
        vm.CommitHistory();

        vm.UndoCommand.Execute(null);
        Assert.Equal(string.Empty, vm.SearchTerm);

        vm.RedoCommand.Execute(null);
        Assert.Equal("rain", vm.SearchTerm);
        Assert.False(vm.CanRedo);
    }

    [Fact]
    public void Undo_RaisesASingleChange()
    {
        var vm = Recorded();
        vm.SetSlot(dayOfWeek: 2, hour: 21);
        vm.CommitHistory();
        int changes = 0;
        vm.FiltersChanged += (_, _) => changes++;

        vm.UndoCommand.Execute(null);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void UndoingAHeatmapClick_PutsEveryDayAndHourBack()
    {
        var vm = Recorded();
        vm.SetSlot(dayOfWeek: 2, hour: 21);
        vm.CommitHistory();

        vm.UndoCommand.Execute(null);

        Assert.All(vm.Days, d => Assert.True(d.IsSelected));
        Assert.Equal(0, vm.StartHour);
        Assert.Equal(23, vm.EndHour);
    }

    [Fact]
    public void ABurstOfEdits_BetweenRecomputes_IsOneStep()
    {
        var vm = Recorded();
        vm.SearchTerm = "r";
        vm.SearchTerm = "ra";
        vm.SearchTerm = "rain";
        vm.CommitHistory();

        vm.UndoCommand.Execute(null);

        Assert.Equal(string.Empty, vm.SearchTerm);
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void Undo_TakesBackAChangeNotYetRecorded()
    {
        var vm = Recorded();
        vm.SearchTerm = "rain";
        vm.CommitHistory();
        vm.MinSeconds = 30;

        vm.UndoCommand.Execute(null);

        Assert.Equal(5, vm.MinSeconds);
        Assert.Equal("rain", vm.SearchTerm);

        vm.RedoCommand.Execute(null);
        Assert.Equal(30, vm.MinSeconds);
    }

    [Fact]
    public void ANewChange_AfterUndo_DropsWhatCouldBeRedone()
    {
        var vm = Recorded();
        vm.SearchTerm = "rain";
        vm.CommitHistory();
        vm.UndoCommand.Execute(null);
        vm.CommitHistory();

        vm.IncludePodcasts = true;
        vm.CommitHistory();

        Assert.False(vm.CanRedo);
        Assert.True(vm.CanUndo);
    }

    [Fact]
    public void RecordingTheSameFiltersTwice_AddsNoStep()
    {
        var vm = Recorded();
        vm.CommitHistory();
        vm.SearchTerm = "rain";
        vm.SearchTerm = string.Empty;
        vm.CommitHistory();

        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void TheHistory_KeepsOnlyTheLatestSteps()
    {
        var vm = Recorded();
        for (int i = 1; i <= FilterViewModel.MaxHistory + 5; i++)
        {
            vm.MinSeconds = i;
            vm.CommitHistory();
        }

        int steps = 0;
        while (vm.CanUndo)
        {
            vm.UndoCommand.Execute(null);
            steps++;
        }

        Assert.Equal(FilterViewModel.MaxHistory, steps);
        Assert.Equal(5, vm.MinSeconds);
    }

    [Fact]
    public void Undo_ShowsTheSavedSetTheFiltersCameFrom()
    {
        var vm = Recorded();
        vm.SearchTerm = "rain";
        vm.NewPresetName = "Rainy";
        vm.SavePresetCommand.Execute(null);
        vm.CommitHistory();
        vm.SearchTerm = "sun";
        vm.CommitHistory();
        Assert.Null(vm.SelectedPreset);

        vm.UndoCommand.Execute(null);

        Assert.Equal("Rainy", vm.SelectedPreset?.Name);
        Assert.Equal("rain", vm.SearchTerm);
    }

    [Fact]
    public void Undo_CoversExclusionsDevicesAndCountries()
    {
        var vm = Recorded();
        vm.SetAvailablePlayback(new[] { ("Mobile", "Mobile", "Mobile") }, new[] { ("CA", "CA", "Canada") });
        vm.ExcludeArtist("Loud Band");
        vm.Devices[0].IsSelected = false;
        vm.Countries[0].IsSelected = false;
        vm.CommitHistory();

        vm.UndoCommand.Execute(null);

        Assert.Empty(vm.ExcludedArtists);
        Assert.True(vm.Devices[0].IsSelected);
        Assert.True(vm.Countries[0].IsSelected);
        Assert.Empty(vm.ToOptions().ExcludedDevices);
    }
}

/// <summary>The history as the main window uses it.</summary>
[Collection(WpfCollection.Name)]
public class FilterHistoryWindowTests
{
    [Fact]
    public async Task ClearingEveryFilter_LeavesTheBarUpToUndoIt()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(new AppSettings());
            await vm.OpenSampleCommand.ExecuteAsync(null);
            Assert.False(vm.ShowFilterBar);
            string everyPlay = vm.TotalPlaysText;

            vm.Filters.SearchTerm = "a";
            await Wait.UntilAsync(() => vm.Filters.CanUndo);
            vm.Filters.ResetCommand.Execute(null);
            await Wait.UntilAsync(() => vm.TotalPlaysText == everyPlay);

            Assert.False(vm.HasActiveFilters);
            Assert.True(vm.ShowFilterBar);

            vm.Filters.UndoCommand.Execute(null);
            Assert.Equal("a", vm.Filters.SearchTerm);
            Assert.True(vm.HasActiveFilters);
        });
    }

    [Fact]
    public void TheMouseBackButton_UndoesAFilterChange()
    {
        WpfTestHost.Run(() =>
        {
            var window = new MainWindow();
            var filters = ((MainViewModel)window.DataContext).Filters;
            filters.ResetCommand.Execute(null);
            filters.CommitHistory();
            filters.SearchTerm = "rain";
            filters.CommitHistory();

            window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.XButton1)
            {
                RoutedEvent = Mouse.MouseUpEvent,
            });
            Assert.Equal(string.Empty, filters.SearchTerm);

            window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.XButton2)
            {
                RoutedEvent = Mouse.MouseUpEvent,
            });
            Assert.Equal("rain", filters.SearchTerm);
        });
    }
}
