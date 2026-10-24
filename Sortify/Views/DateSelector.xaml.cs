using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace Sortify.Views;

/// <summary>One option in a <see cref="DateSelector"/> drop-down. A value of 0 means "Any".</summary>
public sealed record DatePart(int Value, string Label);

/// <summary>
/// Picks an optional date with separate year, month and day drop-downs instead of a calendar
/// popup. Choosing "Any" for the year clears the date; picking a year while the date is empty
/// fills in the month and day from <see cref="FillToEnd"/>.
/// </summary>
public partial class DateSelector : UserControl
{
    /// <summary>The first year offered; Spotify launched in 2008, so nothing older can exist.</summary>
    internal const int FirstYear = 2008;

    public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(
        nameof(SelectedDate), typeof(DateTime?), typeof(DateSelector),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((DateSelector)d).ShowDate(), null, false, UpdateSourceTrigger.PropertyChanged));

    public static readonly DependencyProperty FillToEndProperty = DependencyProperty.Register(
        nameof(FillToEnd), typeof(bool), typeof(DateSelector), new PropertyMetadata(false));

    private bool _syncing;

    public DateSelector()
    {
        InitializeComponent();
        MonthBox.ItemsSource = Enumerable.Range(1, 12)
            .Select(m => new DatePart(m, CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(m)))
            .ToList();
        Loaded += (_, _) => NameParts();
        ShowDate();
    }

    /// <summary>The chosen day, or null when the year is "Any".</summary>
    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    /// <summary>
    /// When true, a newly picked year or month starts at its last day (for "to" dates) rather
    /// than its first.
    /// </summary>
    public bool FillToEnd
    {
        get => (bool)GetValue(FillToEndProperty);
        set => SetValue(FillToEndProperty, value);
    }

    /// <summary>Gives each drop-down a screen-reader name built from the control's own.</summary>
    private void NameParts()
    {
        var name = AutomationProperties.GetName(this);
        if (string.IsNullOrEmpty(name))
            return;
        AutomationProperties.SetName(YearBox, $"{name} year");
        AutomationProperties.SetName(MonthBox, $"{name} month");
        AutomationProperties.SetName(DayBox, $"{name} day");
    }

    private void ShowDate()
    {
        _syncing = true;
        try
        {
            var date = SelectedDate;
            var years = YearOptions(date?.Year);
            YearBox.ItemsSource = years;
            if (date is { } d)
            {
                YearBox.SelectedItem = years.First(y => y.Value == d.Year);
                MonthBox.SelectedIndex = d.Month - 1;
                var days = DayOptions(d.Year, d.Month);
                DayBox.ItemsSource = days;
                DayBox.SelectedIndex = d.Day - 1;
            }
            else
            {
                YearBox.SelectedIndex = 0;
                MonthBox.SelectedIndex = -1;
                DayBox.ItemsSource = null;
            }
            MonthBox.IsEnabled = DayBox.IsEnabled = date is not null;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnPartChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing)
            return;

        if (YearBox.SelectedItem is not DatePart { Value: > 0 } year)
        {
            SelectedDate = null;
            return;
        }

        // A new month or year keeps the day where it fits, otherwise moves to the month's end.
        var month = (MonthBox.SelectedItem as DatePart)?.Value ?? (FillToEnd ? 12 : 1);
        var lastDay = DateTime.DaysInMonth(year.Value, month);
        var day = DayBox.SelectedItem is DatePart picked && SelectedDate is not null
            ? Math.Min(picked.Value, lastDay)
            : FillToEnd ? lastDay : 1;
        var date = new DateTime(year.Value, month, day);
        if (SelectedDate == date)
            ShowDate();
        else
            SelectedDate = date;
    }

    /// <summary>"Any", then the newest year first, back to <see cref="FirstYear"/> (or further for an older date).</summary>
    internal static List<DatePart> YearOptions(int? include)
    {
        var newest = Math.Max(DateTime.Today.Year, include ?? 0);
        var oldest = Math.Min(FirstYear, include ?? FirstYear);
        var options = new List<DatePart> { new(0, "Any") };
        for (var y = newest; y >= oldest; y--)
            options.Add(new DatePart(y, y.ToString(CultureInfo.CurrentCulture)));
        return options;
    }

    internal static List<DatePart> DayOptions(int year, int month)
        => Enumerable.Range(1, DateTime.DaysInMonth(year, month))
            .Select(d => new DatePart(d, d.ToString(CultureInfo.CurrentCulture)))
            .ToList();
}
