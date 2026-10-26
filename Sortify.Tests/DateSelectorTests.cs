using System.Windows.Controls;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>The year / month / day drop-downs that replace the calendar date pickers.</summary>
[Collection(WpfCollection.Name)]
public class DateSelectorTests
{
    private static DatePart Option(ComboBox box, int value) =>
        box.Items.Cast<DatePart>().First(p => p.Value == value);

    [Fact]
    public void ShowsTheBoundDateInEachDropDown()
    {
        WpfTestHost.Run(() =>
        {
            var selector = new DateSelector { SelectedDate = new DateTime(2021, 3, 14) };

            Assert.Equal(2021, ((DatePart)selector.YearBox.SelectedItem).Value);
            Assert.Equal(3, ((DatePart)selector.MonthBox.SelectedItem).Value);
            Assert.Equal(14, ((DatePart)selector.DayBox.SelectedItem).Value);
            Assert.Equal(31, selector.DayBox.Items.Count);
        });
    }

    [Fact]
    public void EmptyDateShowsAnyAndDisablesMonthAndDay()
    {
        WpfTestHost.Run(() =>
        {
            var selector = new DateSelector();

            Assert.Equal("Any", ((DatePart)selector.YearBox.SelectedItem).Label);
            Assert.False(selector.MonthBox.IsEnabled);
            Assert.False(selector.DayBox.IsEnabled);
        });
    }

    [Fact]
    public void PickingAYearFillsTheStartOrEndOfIt()
    {
        WpfTestHost.Run(() =>
        {
            var from = new DateSelector();
            from.YearBox.SelectedItem = Option(from.YearBox, 2019);
            Assert.Equal(new DateTime(2019, 1, 1), from.SelectedDate);
            Assert.True(from.MonthBox.IsEnabled);

            var to = new DateSelector { FillToEnd = true };
            to.YearBox.SelectedItem = Option(to.YearBox, 2019);
            Assert.Equal(new DateTime(2019, 12, 31), to.SelectedDate);
        });
    }

    [Fact]
    public void ChangingMonthClampsTheDayAndAnyClears()
    {
        WpfTestHost.Run(() =>
        {
            var selector = new DateSelector { SelectedDate = new DateTime(2020, 1, 31) };

            selector.MonthBox.SelectedItem = Option(selector.MonthBox, 2);
            Assert.Equal(new DateTime(2020, 2, 29), selector.SelectedDate);
            Assert.Equal(29, selector.DayBox.Items.Count);

            selector.DayBox.SelectedItem = Option(selector.DayBox, 10);
            Assert.Equal(new DateTime(2020, 2, 10), selector.SelectedDate);

            selector.YearBox.SelectedItem = Option(selector.YearBox, 0);
            Assert.Null(selector.SelectedDate);
        });
    }

    [Fact]
    public void OffersAnOlderBoundYear()
    {
        Assert.Contains(DateSelector.YearOptions(2001), p => p.Value == 2001);
        Assert.Equal(DateSelector.FirstYear, DateSelector.YearOptions(null)[^1].Value);
    }
}
