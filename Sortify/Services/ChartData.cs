using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace Sortify.Services;

/// <summary>
/// One chart's worth of LiveCharts state. Bundling the series with its axes keeps the view
/// model to a single property per chart instead of three, and means a chart can never be
/// left showing new series against stale axes.
/// </summary>
public sealed class ChartData
{
    public ISeries[] Series { get; init; } = Array.Empty<ISeries>();
    public Axis[] XAxes { get; init; } = Array.Empty<Axis>();
    public Axis[] YAxes { get; init; } = Array.Empty<Axis>();

    /// <summary>
    /// The row behind each bar, in the same order as the series values, so a clicked bar can
    /// be traced back to what it stands for. Empty for charts whose points aren't rows.
    /// </summary>
    public IReadOnlyList<object> Items { get; init; } = Array.Empty<object>();

    /// <summary>Placeholder for a chart with nothing loaded yet.</summary>
    public static ChartData Empty { get; } = new();
}
