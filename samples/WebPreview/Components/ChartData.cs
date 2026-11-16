namespace WebPreview.Components
{
    // Data of the chart (wwwroot/js/chart.js), serialized as JSON with camel-case names

    /// <param name="FillMode"><c>line</c>, <c>area</c>, <c>baseline</c> or <c>histogram</c>.</param>
    public sealed record ChartSeries(string Id, string Name, string LineColor, string FillColor, string FillMode);

    /// <summary>Series of an indicator drawn over the candles.</summary>
    /// <param name="Pane">Pane of the series: 0 over the candles, 1 below them with its own price scale (oscillators).</param>
    public sealed record ChartOverlay(ChartSeries Config, IReadOnlyList<double?> Values, int Pane = 0);

    /// <param name="Position"><c>aboveBar</c> or <c>belowBar</c>.</param>
    /// <param name="Shape"><c>arrowUp</c>, <c>arrowDown</c> or <c>circle</c>.</param>
    public sealed record ChartMarker(int Index, string Position, string Shape, string Color, string Text);

    /// <summary>A rectangle around the candles from <paramref name="From"/> to <paramref name="To"/>.</summary>
    public sealed record ChartPattern(int From, int To, string Color, string Label);

    /// <summary>Histogram in a pane below the candles, e.g. the final proposal of the strategies.</summary>
    public sealed record ChartHistogram(string Label, IReadOnlyList<double> Values);

    public static class Palette
    {
        public const string Green = "#2fbf71";
        public const string Red = "#e5534b";
    }
}
