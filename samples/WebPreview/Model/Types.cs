namespace WebPreview.Model
{
    /// <summary>Standards supported by the preview.</summary>
    public enum StandardId { Core, Stock }

    /// <summary>Context types of the standards (docs/core/context.md, docs/standards/stock/README.md).</summary>
    public enum ContextKind { Default, Iteration, Time, Stock, Indicator, Pattern, Strategy }

    public sealed record StandardDefinition(StandardId Id, string Name, string Identifier, IReadOnlyList<ContextDefinition> Contexts);

    /// <summary>A constant provided by a context type, for the suggestions of the editor.</summary>
    public sealed record ContextConstant(string Name, string Detail);

    public sealed record ContextDefinition(
        ContextKind Kind,
        StandardId Standard,
        string Name,
        string Description,
        IReadOnlyList<ContextConstant> Constants);

    /// <summary>A formula of a context type.</summary>
    public record Example(string Id, string Name, string Description, string Formula);

    // Pattern Context

    public enum PatternType { Bullish, Bearish }

    public enum PatternColor { Green, Red }

    /// <param name="PatternId">Identifier of the pattern in the registry: <c>LOAD_PATTERN("ENGULFING")</c>.</param>
    /// <param name="Length">Number of candles of the pattern, ending with the candle where it is detected (width of its rectangle).</param>
    public sealed record PatternConfig(
        string Id, string Name, string Description, string Formula,
        string PatternId,
        IReadOnlyList<IndicatorParameterConfig> Parameters,
        PatternType Type, PatternColor Color, int Length)
        : Example(Id, Name, Description, Formula);

    // Indicator Context

    /// <summary>A parameter of a definition of the registry (an indicator or a pattern).</summary>
    /// <param name="Id">Key in <c>PARAMETERS</c>.</param>
    public sealed record IndicatorParameterConfig(string Id, string Name, double DefaultValue);

    public enum FillMode { Line, Area, Baseline, Histogram }

    /// <param name="Id">Key of the series in an Object result (<see cref="SeriesMode.Multiple"/> mode).</param>
    public sealed record SeriesConfig(string Id, string Name, string Description, string LineColor, string FillColor, FillMode FillMode);

    /// <summary><c>Single</c>: the formula returns a Number per candle; <c>Multiple</c>: an Object with one Number per series.</summary>
    public enum SeriesMode { Single, Multiple }

    /// <summary>
    /// <c>Normal</c>: drawn over the candles, on their price scale (moving averages, bands).
    /// <c>Oscillator</c>: drawn in a separate pane below the candles, with its own price scale (MACD, RSI).
    /// </summary>
    public enum IndicatorType { Normal, Oscillator }

    /// <param name="IndicatorId">Identifier of the indicator in the registry: <c>LOAD_INDICATOR("SMA")</c>.</param>
    public sealed record IndicatorConfig(
        string Id, string Name, string Description, string Formula,
        string IndicatorId,
        IReadOnlyList<IndicatorParameterConfig> Parameters,
        IReadOnlyList<SeriesConfig> Series,
        SeriesMode SeriesMode,
        IndicatorType Type)
        : Example(Id, Name, Description, Formula);

    // Strategy Context

    /// <param name="Confidence">Multiplier of the signal of the strategy (default 1.0).</param>
    /// <param name="Lifetime">Number of candles a signal stays active, starting with the candle where it is given.</param>
    public sealed record StrategyConfig(
        string Id, string Name, string Description, string Formula,
        double Confidence, int Lifetime)
        : Example(Id, Name, Description, Formula);

    /// <summary>A candle of the chart; <see cref="Time"/> in milliseconds (UTC).</summary>
    public sealed record ChartCandle(double Time, double Open, double High, double Low, double Close, double Volume);

    public static class Names
    {
        /// <summary>Lower-case name of an enum value, as used in the CSS classes and the options of the preview.</summary>
        public static string Css<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

        /// <summary>The CSS classes that are not null; null (no attribute) if there is none.</summary>
        public static string? Classes(params string?[] classes)
        {
            string joined = string.Join(' ', classes.Where(c => !string.IsNullOrEmpty(c)));
            return joined.Length == 0 ? null : joined;
        }

        public static string Number(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static string Fixed(double value, int digits) => value.ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>A number typed into an input; NaN if it is not a number (like <c>Number(value)</c> in JavaScript).</summary>
        public static double Parse(object? value)
        {
            string text = value?.ToString()?.Trim() ?? "";
            if (text.Length == 0)
                return 0;
            return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number)
                ? number
                : double.NaN;
        }
    }
}
