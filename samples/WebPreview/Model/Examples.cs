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

        private static readonly ContextConstant[] TimeConstants =
        [
            new("TIME_S", "time from the first frame, in seconds"),
            new("TIME_MS", "time from the first frame, in milliseconds"),
            new("DTIME_S", "time from the previous frame, in seconds; 0 in the first frame"),
            new("DTIME_MS", "time from the previous frame, in milliseconds"),
            new("FRAME", "index of the current frame (follows @array_indexing)"),
            new("PREV", "result of the previous frame; null in the first one"),
        ];

        public static readonly IReadOnlyList<StandardDefinition> Standards =
        [
            new(StandardId.Core, "Core 1.0", "core-1.0",
            [
                new(ContextKind.Default, StandardId.Core, "Default Context",
                    "Executes the formula once. The result is the value of the last sequence.", []),
                new(ContextKind.Iteration, StandardId.Core, "Iteration Context",
                    "Executes the formula once per iteration; every iteration starts without variables.", IterationConstants),
                new(ContextKind.Time, StandardId.Core, "Time Context",
                    "Executes the formula once per frame as time passes: animations, simulations, clocks.", TimeConstants),
            ]),
            new(StandardId.Stock, "Stock 1.0", "stock-1.0",
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
                    "Returns a Boolean for every candle: whether the pattern ends at it.",
                    [
                        .. StockConstants,
                        new("PARAMETERS", "parameters of the pattern, completed with the defaults"),
                    ]),
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
            new("text", "Text functions", "Text functions; a character is one Unicode code point.",
                """
                $name = "  formulang  "
                $clean = UPPERCASE(TRIM($name))
                $word = "Zażółć gęślą jaźń"

                [$clean, LEN($clean), SPLIT("a,b,c", ","), REPLACE($clean, "LANG", "-LANG"), LEN($word), REVERSE($word)]
                """),
            new("format", "Formatting", "FORMAT with sequential, indexed and named placeholders.",
                """
                $items = [{ name: "Coffee", price: 12.5 }, { name: "Croissant", price: 7.25 }]
                $total = SUM(MAP($items, (e) => e.price))

                $lines = MAP($items, (e, i) => FORMAT("{0}. {1} – {2:.2} zł", [i + 1, e.name, e.price]))
                $summary = FORMAT("Total: {total:.2} zł ({count} items)", { total: $total, count: LEN($items) })

                JOIN($lines, "\n") + "\n" + $summary
                """),
            new("time", "Time functions", "NOW, the parts of a date, DATE and FORMAT_TIME.",
                """
                <# NOW is the moment of the execution: Unix time in milliseconds, UTC #>
                @time_offset = 60    # local time UTC+1, only for this execution

                $days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]
                $today = DATE(YEAR(NOW), MONTH(NOW), DAY(NOW))
                $new_year = DATE(YEAR(NOW) + 1, 1, 1)

                FORMAT("{} {} · {} days until the new year", [
                    $days[WEEKDAY(NOW) - 1],
                    FORMAT_TIME(NOW, "DD.MM.YYYY hh:mm:ss"),
                    ($new_year - $today) / (24 * 60 * 60 * 1000)
                ])
                """),
            new("comments", "Comments", "Line comments and scoped comments that may span several lines.",
                """
                $a = 14          # a line comment ends with the line
                $b = <# a scoped comment #> 24

                <#
                A scoped comment may span
                several lines.
                #>

                $a + $b
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

        private static readonly Example[] TimeExamples =
        [
            new("motion", "Moving point", "Moves with 5 units per second and stops at 100; the position is carried in PREV.",
                """
                $speed = 5
                $position = IF(ISNULL(PREV), 0, PREV)
                MIN($position + $speed * DTIME_S, 100)
                """),
            new("constants", "Constants", "The constants of every frame.", "[FRAME, ROUND(TIME_S, 2), ROUND(DTIME_MS, 1)]"),
            new("wave", "Sine wave", "A value that oscillates between -1 and 1 every 4 seconds.", "ROUND(SIN(TIME_S / 4 * 2 * PI), 3)"),
            new("rotation", "Rotation", "An angle that repeats every 4 seconds.", "ROUND(MOD(TIME_S / 4 * 360, 360), 1)"),
            new("clock", "Clock", "NOW is set again for every frame.", "FORMAT_TIME(NOW, \"hh:mm:ss.iii\")"),
            new("stopwatch", "Stopwatch", "Frames per second measured from DTIME_S.",
                """
                $fps = IF(DTIME_S == 0, 0, 1 / DTIME_S)
                FORMAT("{:.1} s · frame {} · {:.0} fps", [TIME_S, FRAME, $fps])
                """),
        ];

        // Stock

        private static readonly Example[] StockExamples =
        [
            new("range", "Candle range", "Difference of the highest and the lowest price of the candle.", "ROUND(HIGH[I] - LOW[I], 2)"),
            new("change", "Change %", "Change of the closing price from the previous candle.",
                "IF(I == @array_indexing, 0, ROUND((CLOSE[I] - CLOSE[I - 1]) / CLOSE[I - 1] * 100, 2))"),
            new("candle", "Current candle", "The candle itself, as an Object<Stock.Candle>.", "@include_object_class = true\nCANDLES[I]"),
            new("running-max", "Running maximum", "The highest close so far, carried in PREV.", "IF(ISNULL(PREV), CLOSE[I], MAX(PREV, CLOSE[I]))"),
            new("direction", "Direction", "IS_BULLISH and IS_BEARISH; a doji is neither.",
                "IF(IS_BULLISH(CANDLES[I]), \"bullish\", IF(IS_BEARISH(CANDLES[I]), \"bearish\", \"doji\"))"),
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
                $short = LOAD_INDICATOR("EMA", { period: PARAMETERS.short_period }, SOURCE)
                $long = LOAD_INDICATOR("EMA", { period: PARAMETERS.long_period }, SOURCE)

                # MACD line for every candle: the two series joined element by element
                $line = MAP($short, (s, i) => IF(OR(ISNULL(s), ISNULL($long[i])), null, s - $long[i]))

                $macd = $line[I]
                $signal = LOAD_INDICATOR("EMA", { period: PARAMETERS.signal_period }, $line)[I]

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
                "An empty indicator: return a Number for every candle. Other formulas load it with LOAD_INDICATOR(\"CUSTOM\").",
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
                $covers = AND($curr.open <= $prev.close, $curr.close >= $prev.open)

                AND(I > @array_indexing, AND(IS_BEARISH($prev), AND(IS_BULLISH($curr), $covers)))
                """,
                "ENGULFING", [],
                PatternType.Bullish, PatternColor.Green, 2),
            new("bearish-engulfing", "Bearish engulfing", "A bearish candle whose body covers the body of the previous bullish candle.",
                Previous + "\n\n" +
                """
                $covers = AND($curr.open >= $prev.close, $curr.close <= $prev.open)

                AND(I > @array_indexing, AND(IS_BULLISH($prev), AND(IS_BEARISH($curr), $covers)))
                """,
                "BEARISH_ENGULFING", [],
                PatternType.Bearish, PatternColor.Red, 2),
            new("hammer", "Hammer", "A small body at the top of the candle with a long lower shadow.",
                """
                $c = CANDLES[I]
                $body = ABS($c.close - $c.open)
                $range = $c.high - $c.low
                $lower = MIN($c.open, $c.close) - $c.low
                $upper = $c.high - MAX($c.open, $c.close)

                AND($range > 0, AND($lower >= PARAMETERS.shadow * $body, $upper <= PARAMETERS.max_upper * $range))
                """,
                "HAMMER", [new("shadow", "Lower shadow × body", 2), new("max_upper", "Max upper shadow (of range)", 0.15)],
                PatternType.Bullish, PatternColor.Green, 1),
            new("shooting-star", "Shooting star", "A small body at the bottom of the candle with a long upper shadow.",
                """
                $c = CANDLES[I]
                $body = ABS($c.close - $c.open)
                $range = $c.high - $c.low
                $lower = MIN($c.open, $c.close) - $c.low
                $upper = $c.high - MAX($c.open, $c.close)

                AND($range > 0, AND($upper >= PARAMETERS.shadow * $body, $lower <= PARAMETERS.max_lower * $range))
                """,
                "SHOOTING_STAR", [new("shadow", "Upper shadow × body", 2), new("max_lower", "Max lower shadow (of range)", 0.15)],
                PatternType.Bearish, PatternColor.Red, 1),
            new("three-soldiers", "Three white soldiers", "Bullish candles in a row, each closing higher than the previous one.",
                """
                $n = PARAMETERS.count
                $from = MAX(I - $n + 1, @array_indexing)

                # Every candle of the window is bullish and closes higher than the previous one
                $rising = (k) => AND(IS_BULLISH(CANDLES[k]), IF(k == $from, true, CLOSE[k] > CLOSE[MAX(k - 1, @array_indexing)]))

                AND(I - $n + 1 >= @array_indexing, ALL(SEQ($n, 1, $from), $rising))
                """,
                "THREE_SOLDIERS", [new("count", "Candles", 3)],
                PatternType.Bullish, PatternColor.Green, 3),
            new(NewFormula, NewExample.Name,
                "An empty pattern: return true for the candle where the pattern ends. Other formulas load it with LOAD_PATTERN(\"CUSTOM\").", "",
                "CUSTOM", [],
                PatternType.Bullish, PatternColor.Green, 1),
        ];

        public static readonly IReadOnlyList<StrategyConfig> Strategies =
        [
            new("crossover", "Moving average crossover", "Buys when the fast SMA crosses above the slow SMA, sells when it crosses below.",
                """
                $fast = LOAD_INDICATOR("SMA", { period: 10 })
                $slow = LOAD_INDICATOR("SMA", { period: 30 })

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
                $b = LOAD_INDICATOR("BOLLINGER")[I]

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
            new("pattern-trend", "Engulfing above the trend", "Buys on a bullish engulfing pattern (LOAD_PATTERN) above the 20-candle SMA.",
                """
                $engulfing = LOAD_PATTERN("ENGULFING")[I]
                $sma = LOAD_INDICATOR("SMA", { period: 20 })[I]

                IF(ISNULL($sma),
                   NEUTRAL,
                   IF(AND($engulfing, CLOSE[I] > $sma), BUY, NEUTRAL))
                """,
                1, 4),
            new(NewFormula, "New strategy", "An empty strategy: return a signal from SELL (-1) to BUY (1) for every candle.", "",
                1, 1),
        ];

        /// <summary>Examples of every context type.</summary>
        public static readonly IReadOnlyDictionary<ContextKind, IReadOnlyList<Example>> ByContext = new Dictionary<ContextKind, IReadOnlyList<Example>>
        {
            [ContextKind.Default] = [.. DefaultExamples, NewExample],
            [ContextKind.Iteration] = [.. IterationExamples, NewExample],
            [ContextKind.Time] = [.. TimeExamples, NewExample],
            [ContextKind.Stock] = [.. StockExamples, NewExample],
            [ContextKind.Indicator] = [.. Indicators],
            [ContextKind.Pattern] = [.. Patterns],
            [ContextKind.Strategy] = [.. Strategies],
        };

        /// <summary>Key of the formula of an example in the state of the preview.</summary>
        public static string FormulaKey(ContextKind kind, string id) => $"{Names.Css(kind)}:{id}";
    }
}
