namespace WebPreview.Model
{
    /// <summary>Standards, context types and their examples: the configurations of the patterns, indicators and strategies.</summary>
    public static class Examples
    {
        private static readonly ContextConstant[] IterationConstants =
        [
            new("I", "index of the current iteration (follows @array_indexing)"),
            new("N", "number of iterations"),
            new("PREV", "result of the previous iteration; null in the first one"),
        ];

        private static readonly ContextConstant[] StockConstants =
        [
            .. IterationConstants,
            new("CANDLES", "Array<Stock.Candle>, oldest first"),
            new("OPEN", "opening prices"),
            new("HIGH", "highest prices"),
            new("LOW", "lowest prices"),
            new("CLOSE", "closing prices"),
            new("VOLUME", "volumes (Number or null)"),
            new("TIME", "Unix time in milliseconds (Number or null)"),
        ];

        public static readonly IReadOnlyList<StandardDefinition> Standards =
        [
            new(StandardId.Core, "Core 0.3", "core-0.3",
            [
                new(ContextKind.Default, StandardId.Core, "Default Context",
                    "Executes the formula once. The result is the value of the last sequence.", []),
                new(ContextKind.Iteration, StandardId.Core, "Iteration Context",
                    "Executes the formula once per iteration; every iteration starts without variables.", IterationConstants),
            ]),
            new(StandardId.Stock, "Stock 0.1", "stock-0.1",
            [
                new(ContextKind.Stock, StandardId.Stock, "Stock Context",
                    "Iterates over the candles of the chart; I is the current candle.", StockConstants),
                new(ContextKind.Indicator, StandardId.Stock, "Indicator Context",
                    "Computes a value (or an object of values) for every candle: the series of an indicator.",
                    [
                        .. StockConstants,
                        new("PARAMETERS", "parameters of the indicator, completed with the defaults"),
                        new("SOURCE", "source values; CLOSE by default"),
                    ]),
                new(ContextKind.Pattern, StandardId.Stock, "Pattern Context",
                    "Returns a Boolean for every candle: whether the pattern ends at it.", StockConstants),
                new(ContextKind.Strategy, StandardId.Stock, "Strategy Context",
                    "Returns a signal from SELL (-1) to BUY (1) for every candle.",
                    [
                        .. StockConstants,
                        new("BUY", "1"),
                        new("NEUTRAL", "0"),
                        new("SELL", "-1"),
                    ]),
            ]),
        ];

        public static readonly IReadOnlyList<ContextDefinition> Contexts = Standards.SelectMany(s => s.Contexts).ToArray();

        /// <summary>Identifier of the empty formula that every context type offers next to its examples.</summary>
        public const string NewFormula = "new";

        private static readonly Example NewExample = new(NewFormula, "New formula", "An empty formula to write from scratch.", "");

        // Core

        private static readonly Example[] DefaultExamples =
        [
            new("circle", "Circle area", "Initial assignment and a constant of the environment.", "$r := 2.5\nPI * $r ^ 2"),
            new("interest", "Compound interest", "Variables, rounding and text concatenation.",
                """
                # Compound interest calculator
                $principal := 1000
                $rate := 0.05
                $years := 10

                $multiplier := (1 + $rate) ^ $years
                $result := ROUND($principal * $multiplier, 2)

                "Final amount: " + TEXT($result)
                """),
            new("statistics", "Statistics", "Arrays, lambdas and an object literal.",
                """
                # Mean and standard deviation of a data set
                $data = [2, 4, 4, 4, 5, 5, 7, 9]
                $mean = AVG($data)
                $stdev = SQRT(SUM(MAP($data, (x) => (x - $mean) ^ 2)) / LEN($data))

                { mean: $mean, stdev: $stdev, above_mean: FILTER($data, (x) => x > $mean) }
                """),
            new("text", "Text functions", "Functions of texts from Core 0.3.",
                """
                $name = "  formulang  "
                $clean = UPPERCASE(TRIM($name))
                [$clean, LEN($clean), SPLIT("a,b,c", ","), REPLACE($clean, "LANG", "-LANG")]
                """),
            new("serialization", "Serialization", "Serialization variables changed by the formula.",
                """
                @decimal_place = 2
                @split_object = true
                @include_object_class = true
                { price: 19.999, items: SEQ(3), candle: CANDLE({ open: 1, high: 2, low: 0.5, close: 1.5 }) }
                """),
            new("errors", "Error handling", "An error replaced with null by @suppress_errors.",
                """
                @suppress_errors = true
                $a = DIV(1, 0)
                $b = ISNULL($a)
                IF($b, "the error was suppressed", "no error")
                """),
            new("validation", "Validation errors", "Validation reports every problem at once.",
                """
                $x = (1 + 2
                $y = "not closed
                """),
        ];

        private static readonly Example[] IterationExamples =
        [
            new("constants", "Constants", "The constants of every iteration.", "[I, N, PREV]"),
            new("powers", "Powers of two", "PREV carries the result of the previous iteration.", "IF(ISNULL(PREV), 1, PREV * 2)"),
            new("fibonacci", "Fibonacci", "A pair of numbers carried in PREV.", "IF(ISNULL(PREV), [0, 1], [PREV[1], PREV[0] + PREV[1]])"),
            new("progress", "Progress", "Text built from I and N.", "TEXT(ROUND((I + 1) / N * 100, 0)) + \"%\""),
        ];

        // Stock

        private static readonly Example[] StockExamples =
        [
            new("range", "Candle range", "Difference of the highest and the lowest price of the candle.", "ROUND(HIGH[I] - LOW[I], 2)"),
            new("change", "Change %", "Change of the closing price from the previous candle.",
                "IF(I == @array_indexing, 0, ROUND((CLOSE[I] - CLOSE[I - 1]) / CLOSE[I - 1] * 100, 2))"),
            new("candle", "Current candle", "The candle itself, as an Object<Stock.Candle>.", "@include_object_class = true\nCANDLES[I]"),
            new("running-max", "Running maximum", "The highest close so far, carried in PREV.", "IF(ISNULL(PREV), CLOSE[I], MAX(PREV, CLOSE[I]))"),
        ];

        /// <summary>The last <c>period</c> values of the source, and whether the window is complete.</summary>
        private const string Window =
            """
            $window = RANGE(SOURCE, MAX(I - $period + 1, @array_indexing), I)
            $complete = AND(LEN($window) == $period, LEN(FILTER($window, (e) => ISNULL(e))) == 0)
            """;

        private static SeriesConfig Line(string id, string name, string description, string color)
            => new(id, name, description, color, "transparent", FillMode.Line);

        public static readonly IReadOnlyList<IndicatorConfig> Indicators =
        [
            new("sma", "Simple moving average", "Average of the last `period` values of the source.",
                "$period = PARAMETERS.period\n" + Window + "\n\nIF($complete, AVG($window), null)",
                "SMA",
                [new("period", "Period", 20)],
                [Line("sma", "SMA", "Moving average", "#f2c14e")],
                SeriesMode.Single, IndicatorType.Normal),
            new("ema", "Exponential moving average", "Moving average that weights recent values more; recursive through PREV.",
                "$period = PARAMETERS.period\n$alpha = 2 / ($period + 1)\n$x = SOURCE[I]\n" + Window + "\n\n" +
                """
                IF(ISNULL($x),
                   null,
                   IF(NOT(ISNULL(PREV)),
                      $alpha * $x + (1 - $alpha) * PREV,
                      IF($complete, AVG($window), null)))
                """,
                "EMA",
                [new("period", "Period", 12)],
                [new("ema", "EMA", "Exponential moving average", "#3ddc97", "rgba(61, 220, 151, 0.14)", FillMode.Area)],
                SeriesMode.Single, IndicatorType.Normal),
            new("bollinger", "Bollinger bands", "Moving average with bands `deviations` standard deviations away.",
                "$period = PARAMETERS.period\n$k = PARAMETERS.deviations\n" + Window + "\n\n" +
                """
                $mean = IF($complete, AVG($window), 0)
                $sd = IF($complete, SQRT(AVG(MAP($window, (e) => (e - $mean) ^ 2))), 0)

                IF($complete, { upper: $mean + $k * $sd, middle: $mean, lower: $mean - $k * $sd }, null)
                """,
                "BOLLINGER",
                [new("period", "Period", 20), new("deviations", "Deviations", 2)],
                [
                    Line("upper", "Upper", "Upper band", "#7aa2f7"),
                    Line("middle", "Middle", "Moving average", "#c0caf5"),
                    Line("lower", "Lower", "Lower band", "#7aa2f7"),
                ],
                SeriesMode.Multiple, IndicatorType.Normal),
            new("donchian", "Donchian channel", "Highest high and lowest low of the last `period` candles.",
                """
                $period = PARAMETERS.period
                $from = MAX(I - $period + 1, @array_indexing)

                IF(I - $from + 1 < $period,
                   null,
                   { upper: MAX(RANGE(HIGH, $from, I)), lower: MIN(RANGE(LOW, $from, I)) })
                """,
                "DONCHIAN",
                [new("period", "Period", 20)],
                [Line("upper", "Upper", "Highest high", "#2fbf71"), Line("lower", "Lower", "Lowest low", "#e5534b")],
                SeriesMode.Multiple, IndicatorType.Normal),
            new("typical", "Typical price", "(high + low + close) / 3, filled above and below its average.",
                "(HIGH[I] + LOW[I] + CLOSE[I]) / 3",
                "TYPICAL",
                [],
                [new("typical", "Typical", "Typical price", "#2fbf71", "rgba(229, 83, 75, 0.18)", FillMode.Baseline)],
                SeriesMode.Single, IndicatorType.Normal),
            new("volume", "Volume", "Traded volume of every candle, as a histogram at the bottom of the chart.",
                "VOLUME[I]",
                "VOLUME_BARS",
                [],
                [new("volume", "Volume", "Volume", "#2fbf71", "rgba(47, 191, 113, 0.45)", FillMode.Histogram)],
                SeriesMode.Single, IndicatorType.Normal),
            new("macd", "MACD", "Difference of two EMAs, its signal line and their histogram (docs/standards/stock/examples).",
                """
                $short = INDICATOR("EMA", { period: PARAMETERS.short_period }, SOURCE)
                $long = INDICATOR("EMA", { period: PARAMETERS.long_period }, SOURCE)

                # MACD line for every candle: the two series joined element by element
                $line = MAP($short, (s, i) => IF(OR(ISNULL(s), ISNULL($long[i])), null, s - $long[i]))

                $macd = $line[I]
                $signal = INDICATOR("EMA", { period: PARAMETERS.signal_period }, $line)[I]

                IF(OR(ISNULL($macd), ISNULL($signal)),
                   null,
                   { macd: $macd, signal: $signal, histogram: $macd - $signal })
                """,
                "MACD",
                [new("short_period", "Short period", 12), new("long_period", "Long period", 26), new("signal_period", "Signal period", 9)],
                [
                    new("histogram", "Histogram", "MACD - signal", "#7aa2f7", "rgba(122, 162, 247, 0.55)", FillMode.Histogram),
                    Line("macd", "MACD", "Short EMA - long EMA", "#4be39a"),
                    Line("signal", "Signal", "EMA of the MACD line", "#ff8f70"),
                ],
                SeriesMode.Multiple, IndicatorType.Oscillator),
            new("rsi", "Relative strength index",
                "Average gain against average loss of the last `period` changes, from 0 to 100 (simple averages).",
                """
                $period = PARAMETERS.period
                $ready = I - @array_indexing >= $period

                # Changes of the closing price within the window
                $from = MAX(I - $period + 1, @array_indexing + 1)
                $changes = MAP(SEQ(MAX(I - $from + 1, 0), 1, $from), (k) => CLOSE[k] - CLOSE[k - 1])

                $gain = SUM(MAP($changes, (c) => MAX(c, 0))) / $period
                $loss = SUM(MAP($changes, (c) => MAX(-c, 0))) / $period

                IF(NOT($ready), null, IF($loss == 0, 100, 100 - 100 / (1 + $gain / $loss)))
                """,
                "RSI",
                [new("period", "Period", 14)],
                [new("rsi", "RSI", "Relative strength index", "#c9a7ff", "rgba(201, 167, 255, 0.12)", FillMode.Area)],
                SeriesMode.Single, IndicatorType.Oscillator),
            new(NewFormula, NewExample.Name,
                "An empty indicator: return a Number for every candle. It is in the registry as INDICATOR(\"CUSTOM\").",
                "",
                "CUSTOM",
                [],
                [Line("value", "Custom", "Value of the indicator", "#e9c46a")],
                SeriesMode.Single, IndicatorType.Normal),
        ];

        private const string Previous = "$prev = CANDLES[MAX(I - 1, @array_indexing)]\n$curr = CANDLES[I]";

        public static readonly IReadOnlyList<PatternConfig> Patterns =
        [
            new("bullish-engulfing", "Bullish engulfing", "A bullish candle whose body covers the body of the previous bearish candle.",
                "# In the first iteration, the previous candle is the current one\n" + Previous + "\n\n" +
                """
                $prev_bearish = $prev.close < $prev.open
                $curr_bullish = $curr.close > $curr.open
                $covers = AND($curr.open <= $prev.close, $curr.close >= $prev.open)

                AND(I > @array_indexing, AND($prev_bearish, AND($curr_bullish, $covers)))
                """,
                PatternType.Bullish, PatternColor.Green, 2),
            new("bearish-engulfing", "Bearish engulfing", "A bearish candle whose body covers the body of the previous bullish candle.",
                Previous + "\n\n" +
                """
                $prev_bullish = $prev.close > $prev.open
                $curr_bearish = $curr.close < $curr.open
                $covers = AND($curr.open >= $prev.close, $curr.close <= $prev.open)

                AND(I > @array_indexing, AND($prev_bullish, AND($curr_bearish, $covers)))
                """,
                PatternType.Bearish, PatternColor.Red, 2),
            new("hammer", "Hammer", "A small body at the top of the candle with a long lower shadow.",
                """
                $c = CANDLES[I]
                $body = ABS($c.close - $c.open)
                $range = $c.high - $c.low
                $lower = MIN($c.open, $c.close) - $c.low
                $upper = $c.high - MAX($c.open, $c.close)

                AND($range > 0, AND($lower >= 2 * $body, $upper <= 0.15 * $range))
                """,
                PatternType.Bullish, PatternColor.Green, 1),
            new("shooting-star", "Shooting star", "A small body at the bottom of the candle with a long upper shadow.",
                """
                $c = CANDLES[I]
                $body = ABS($c.close - $c.open)
                $range = $c.high - $c.low
                $lower = MIN($c.open, $c.close) - $c.low
                $upper = $c.high - MAX($c.open, $c.close)

                AND($range > 0, AND($upper >= 2 * $body, $lower <= 0.15 * $range))
                """,
                PatternType.Bearish, PatternColor.Red, 1),
            new("three-soldiers", "Three white soldiers", "Three bullish candles in a row, each closing higher than the previous one.",
                """
                $a = CANDLES[MAX(I - 2, @array_indexing)]
                $b = CANDLES[MAX(I - 1, @array_indexing)]
                $c = CANDLES[I]

                $bullish = AND($a.close > $a.open, AND($b.close > $b.open, $c.close > $c.open))
                $rising = AND($b.close > $a.close, $c.close > $b.close)

                AND(I >= @array_indexing + 2, AND($bullish, $rising))
                """,
                PatternType.Bullish, PatternColor.Green, 3),
            new(NewFormula, NewExample.Name, "An empty pattern: return true for the candle where the pattern ends.", "",
                PatternType.Bullish, PatternColor.Green, 1),
        ];

        public static readonly IReadOnlyList<StrategyConfig> Strategies =
        [
            new("crossover", "Moving average crossover", "Buys when the fast SMA crosses above the slow SMA, sells when it crosses below.",
                """
                $fast = INDICATOR("SMA", { period: 10 })
                $slow = INDICATOR("SMA", { period: 30 })

                $p = MAX(I - 1, @array_indexing)
                $values = [$fast[$p], $slow[$p], $fast[I], $slow[I]]
                $ready = AND(I > @array_indexing, LEN(FILTER($values, (e) => ISNULL(e))) == 0)

                IF(NOT($ready),
                   NEUTRAL,
                   IF(AND($fast[$p] <= $slow[$p], $fast[I] > $slow[I]),
                      BUY,
                      IF(AND($fast[$p] >= $slow[$p], $fast[I] < $slow[I]), SELL, NEUTRAL)))
                """,
                1, 5),
            new("bollinger-reversion", "Bollinger reversion", "Expects the price to return inside the Bollinger bands.",
                """
                $b = INDICATOR("BOLLINGER")[I]

                IF(ISNULL($b),
                   NEUTRAL,
                   IF(CLOSE[I] < $b.lower, BUY, IF(CLOSE[I] > $b.upper, SELL, NEUTRAL)))
                """,
                0.6, 3),
            new("momentum", "Momentum", "Follows the price at the moment its change over 10 candles exceeds 6%.",
                """
                $n = 10
                $t = 0.06
                $now = IF(I < $n, 0, (CLOSE[I] - CLOSE[I - $n]) / CLOSE[I - $n])
                $before = IF(I < $n + 1, 0, (CLOSE[I - 1] - CLOSE[I - 1 - $n]) / CLOSE[I - 1 - $n])

                # A signal only when the change crosses the threshold
                IF(AND(ABS($now) > $t, ABS($before) <= $t), SIGN($now), NEUTRAL)
                """,
                0.4, 2),
            new(NewFormula, "New strategy", "An empty strategy: return a signal from SELL (-1) to BUY (1) for every candle.", "",
                1, 1),
        ];

        /// <summary>Examples of every context type.</summary>
        public static readonly IReadOnlyDictionary<ContextKind, IReadOnlyList<Example>> ByContext = new Dictionary<ContextKind, IReadOnlyList<Example>>
        {
            [ContextKind.Default] = [.. DefaultExamples, NewExample],
            [ContextKind.Iteration] = [.. IterationExamples, NewExample],
            [ContextKind.Stock] = [.. StockExamples, NewExample],
            [ContextKind.Indicator] = [.. Indicators],
            [ContextKind.Pattern] = [.. Patterns],
            [ContextKind.Strategy] = [.. Strategies],
        };

        /// <summary>Key of the formula of an example in the state of the preview.</summary>
        public static string FormulaKey(ContextKind kind, string id) => $"{Names.Css(kind)}:{id}";
    }
}
