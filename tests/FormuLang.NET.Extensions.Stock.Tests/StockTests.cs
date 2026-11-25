using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Extensions.Stock.Contexts;
using FormuLang.NET.Extensions.Stock.Registry;
using Env = FormuLang.NET.Core.Structures.Environment;

namespace FormuLang.NET.Extensions.Stock.Tests
{
    /// <summary>
    /// Examples of docs/standards/stock: CANDLE, INDICATOR, indicator examples, pattern and strategy examples.
    /// </summary>
    public class StockTests
    {
        // docs/standards/stock/examples/indicator_examples.md
        private const string Sma = """
            $period = PARAMETERS.period

            # The last `period` values of the source, fewer at the beginning of the chart
            $window = RANGE(SOURCE, MAX(I - $period + 1, @array_indexing), I)
            $complete = AND(LEN($window) == $period, LEN(FILTER($window, (e) => ISNULL(e))) == 0)

            IF($complete, AVG($window), null)
            """;

        private const string Ema = """
            $period = PARAMETERS.period
            $alpha = 2 / ($period + 1)
            $x = SOURCE[I]

            $window = RANGE(SOURCE, MAX(I - $period + 1, @array_indexing), I)
            $complete = AND(LEN($window) == $period, LEN(FILTER($window, (e) => ISNULL(e))) == 0)

            IF(ISNULL($x),
               null,
               IF(NOT(ISNULL(PREV)),
                  $alpha * $x + (1 - $alpha) * PREV,
                  IF($complete, AVG($window), null)))
            """;

        private const string Macd = """
            $short = LOAD_INDICATOR("EMA", { period: PARAMETERS.short_period }, SOURCE)
            $long = LOAD_INDICATOR("EMA", { period: PARAMETERS.long_period }, SOURCE)

            # MACD line for every candle: the two series joined element by element
            $line = MAP($short, (s, i) => IF(OR(ISNULL(s), ISNULL($long[i])), null, s - $long[i]))

            $macd = $line[I]
            $signal = LOAD_INDICATOR("EMA", { period: PARAMETERS.signal_period }, $line)[I]

            IF(OR(ISNULL($macd), ISNULL($signal)),
               null,
               { macd: $macd, signal: $signal, histogram: $macd - $signal })
            """;

        private static Env CreateEnvironment(out FormulaRegistry registry)
        {
            var env = Env.Create();
            registry = env.RegisterStock().Registry;

            registry.Indicators.Add("SMA", Sma, new FormulaParameter("period", 14));
            registry.Indicators.Add("EMA", Ema, new FormulaParameter("period", 14));
            registry.Indicators.Add("MACD", Macd,
                new FormulaParameter("short_period", 12),
                new FormulaParameter("long_period", 26),
                new FormulaParameter("signal_period", 9));

            return env;
        }

        private static Env CreateEnvironment() => CreateEnvironment(out _);

        private static Candle Flat(double price) => new(price, price, price, price);

        /// <summary>
        /// <c>CLOSE</c> = <c>[1, 2, 3, 4, 5]</c>.
        /// </summary>
        private static readonly Candle[] OneToFive = [Flat(1), Flat(2), Flat(3), Flat(4), Flat(5)];

        /// <summary>
        /// Executes the formula in a Stock Context and returns the serialized result of the last iteration.
        /// </summary>
        private static string RunLast(string formula, Candle[]? candles = null, Env? env = null)
        {
            var context = StockContext.Create(env ?? CreateEnvironment(), candles ?? OneToFive);
            IterationExecutionResult result = Executor.Create().ExecuteIterations(formula, context);
            return result.Results[^1].ToString();
        }

        private static void AssertResult(string formula, string expected, Candle[]? candles = null)
        {
            string actual = RunLast(formula, candles);

            if (expected.EndsWith("_ERROR"))
                Assert.StartsWith($"[{expected} (", actual);
            else
                Assert.Equal(expected, actual);
        }

        // Standard

        [Fact]
        public void RegisterStock_AddsTheStandard()
        {
            var env = CreateEnvironment();

            Assert.True(env.Supports("stock-1.0"));
            Assert.Equal("[\"core-1.0\", \"stock-1.0\"]", env.GetProperty("STANDARDS").GetValue().ToString());
            Assert.Contains("Stock.Candle", env.ExtendedTypes);
            Assert.NotNull(env.GetFunction("CANDLE"));
            Assert.NotNull(env.GetFormulaRegistry());
            Assert.True(StockStandard.IsCoreSupported(env));
        }

        [Theory]
        [InlineData("REQUIRE \"stock-1.0\"\nREQUIRE \"core-1+\"\n1", "1")]
        [InlineData("REQUIRE \"stock-1.x\"\nCLOSE[I] - OPEN[I]", "0")]
        public void Require(string formula, string expected) => AssertResult(formula, expected);

        // Stock Context

        [Fact]
        public void StockContext_Constants()
        {
            Candle[] candles = [new(10, 12, 9, 11, 5_000, 1_700_000_000_000), new(11, 13, 10, 12)];

            Assert.Equal("[2, [10, 11], [12, 13], [9, 10], [11, 12], [5000, null], [1700000000000, null]]",
                RunLast("[N, OPEN, HIGH, LOW, CLOSE, VOLUME, TIME]", candles));
            Assert.Equal("[12, 12, 13]", RunLast("[CLOSE[I], CANDLES[I].close, CANDLES[I].high]", candles));
            Assert.Equal("true", RunLast("ISOBJ(CANDLES[0])", candles));
        }

        [Fact]
        public void StockContext_RejectsInvalidCandles()
        {
            Assert.Throws<ArgumentException>(() => StockContext.Create(CreateEnvironment(), [new Candle(10, 9, 8, 11)]));
            Assert.Throws<ArgumentException>(() => StockContext.Create(CreateEnvironment(), [new Candle(10, 12, 8, 11, -1)]));
        }

        [Fact]
        public void StockContext_CandleSerialization()
        {
            Assert.Equal("[Stock.Candle] { open: 10, high: 12, low: 9, close: 11, volume: null, time: null }",
                RunLast("@include_object_class = true\nTEXT(CANDLES[0])", [new Candle(10, 12, 9, 11)]));
        }

        // CANDLE

        [Theory]
        [InlineData("CANDLE({ open: 1_000, high: 1_200, low: 800, close: 1_000 })",
            "[Stock.Candle] { open: 1000, high: 1200, low: 800, close: 1000, volume: null, time: null }")]
        [InlineData("CANDLE({ open: 10, high: 12, low: 9, close: 11, volume: 5_000 })",
            "[Stock.Candle] { open: 10, high: 12, low: 9, close: 11, volume: 5000, time: null }")]
        [InlineData("CANDLE({ open: 1_000, low: 300 })",
            "[OPERATION_ERROR (3)] Object does not meet the requirements to be <Stock.Candle>.")]
        [InlineData("CANDLE({ open: 10, high: 9, low: 8, close: 11 })",
            "[OPERATION_ERROR (3)] Object does not meet the requirements to be <Stock.Candle>.")]
        [InlineData("CANDLE({ open: 10, high: 12, low: 9, close: 11, color: \"red\" })",
            "[OPERATION_ERROR (3)] Object does not meet the requirements to be <Stock.Candle>.")]
        [InlineData("CANDLE({ open: 10, high: 12, low: 9, close: 11, volume: \"5\" })",
            "[OPERATION_ERROR (3)] Object does not meet the requirements to be <Stock.Candle>.")]
        [InlineData("CANDLE({ open: 10, high: 12, low: 9, close: 11, volume: -5 })",
            "[OPERATION_ERROR (3)] Object does not meet the requirements to be <Stock.Candle>.")]
        public void Candle_Examples(string formula, string expected)
        {
            var result = Executor.Create().Execute("@include_object_class = true\n" + formula, CreateEnvironment());
            Assert.Equal(expected, result.ToString());
        }

        [Theory]
        [InlineData("CANDLE(1)", "ARGUMENT_ERROR")]
        [InlineData("KEYS(CANDLE({ open: 1, high: 1, low: 1, close: 1 }))", "[\"open\", \"high\", \"low\", \"close\", \"volume\", \"time\"]")]
        [InlineData("CANDLE({ open: 1, high: 2, low: 1, close: 2 }).high", "2")]
        [InlineData("EQU(CANDLE({ open: 1, high: 1, low: 1, close: 1 }), CANDLES[0])", "true")]
        public void Candle_Values(string formula, string expected) => AssertResult(formula, expected, [Flat(1)]);

        // INDICATOR

        [Theory]
        [InlineData("LOAD_INDICATOR(\"SMA\", { period: 3 })", "[null, null, 2, 3, 4]")]
        [InlineData("LOAD_INDICATOR(\"SMA\", { period: 2 }, [10, 20, 30, 40, 50])", "[null, 15, 25, 35, 45]")]
        [InlineData("LOAD_INDICATOR(\"SMA\", { period: 3 })[I]", "4")]
        [InlineData("LOAD_INDICATOR(\"XYZ\")", "INVALID_KEY_ERROR")]
        [InlineData("LOAD_INDICATOR(\"SMA\", { length: 3 })", "INVALID_KEY_ERROR")]
        [InlineData("LOAD_INDICATOR(\"SMA\", { period: 3 }, [1, 2])", "OPERATION_ERROR")]
        [InlineData("LOAD_INDICATOR(\"SMA\", { period: 3 }, [1, 2, \"3\", 4, 5])", "OPERATION_ERROR")]
        [InlineData("LOAD_INDICATOR(\"SMA\", { period: 2 }, [1, null, 3, 4, 5])", "[null, null, null, 3.5, 4.5]")]
        [InlineData("LOAD_INDICATOR(\"SMA\")", "[null, null, null, null, null]")]
        [InlineData("LOAD_INDICATOR(\"EMA\", { period: 3 })", "[null, null, 2, 3, 4]")]
        [InlineData("LOAD_INDICATOR(1)", "ARGUMENT_ERROR")]
        public void Indicator_Examples(string formula, string expected) => AssertResult(formula, expected);

        [Fact]
        public void Indicator_EnvironmentVariablesOfTheCallerAreNotPassed()
        {
            Assert.Equal("[null, null, 2, 3, 4]", RunLast("@array_indexing = 1\nLOAD_INDICATOR(\"SMA\", { period: 3 })"));
            Assert.Equal("4", RunLast("@array_indexing = 1\nLOAD_INDICATOR(\"SMA\", { period: 3 })[I]"));
        }

        [Fact]
        public void Indicator_OutsideOfAStockContext()
        {
            var result = Executor.Create().Execute("LOAD_INDICATOR(\"SMA\")", CreateEnvironment());
            Assert.Equal("OPERATION_ERROR", result.Error.Name);
        }

        [Fact]
        public void Indicator_ThatCallsItself()
        {
            var env = CreateEnvironment(out var registry);
            registry.Indicators.Add("LOOP", "LOAD_INDICATOR(\"LOOP\")[I]");
            registry.Indicators.Add("A", "LOAD_INDICATOR(\"B\")[I]");
            registry.Indicators.Add("B", "LOAD_INDICATOR(\"A\")[I]");

            Assert.StartsWith("[OPERATION_ERROR (3)] Indicator \"LOOP\" calls itself.", RunLast("LOAD_INDICATOR(\"LOOP\")", env: env));
            Assert.StartsWith("[OPERATION_ERROR (3)] Indicator \"A\" calls itself.", RunLast("LOAD_INDICATOR(\"A\")", env: env));
        }

        [Theory]
        [InlineData("\"text\"")]
        [InlineData("IF(I == 0, 1, { a: 1 })")]
        [InlineData("IF(I == 0, { a: 1 }, { b: 1 })")]
        [InlineData("{ a: \"x\" }")]
        [InlineData("$x = 1")]
        public void Indicator_ResultThatIsNotAllowed(string formula)
        {
            var env = CreateEnvironment(out var registry);
            registry.Indicators.Add("BAD", formula);

            Assert.StartsWith("[OPERATION_ERROR (3)] Iteration", RunLast("LOAD_INDICATOR(\"BAD\")", env: env));
        }

        [Fact]
        public void Indicator_ErrorOfAnIterationIsReturned()
        {
            var env = CreateEnvironment(out var registry);
            registry.Indicators.Add("FAIL", "IF(I == 2, ERROR(\"broken\"), I)");

            Assert.Equal("[CUSTOM_ERROR (1)] broken", RunLast("LOAD_INDICATOR(\"FAIL\")", env: env));
        }

        [Fact]
        public void Indicator_Macd()
        {
            var context = StockContext.Create(CreateEnvironment(), OneToFive);
            IValue series = context.ComputeIndicator("MACD", Params(("short_period", 2), ("long_period", 3), ("signal_period", 2)));

            IValue[] values = (IValue[])series.Raw!;
            Assert.Equal(5, values.Length);
            Assert.All(values[..3], v => Assert.Equal(Types.NullName, v.Type.Base));

            foreach (IValue value in values[3..])
            {
                var obj = (IObject)value.Raw!;
                Assert.Equal(["macd", "signal", "histogram"], obj.Keys);
                Assert.Equal(0.5, (double)obj.GetProperty("macd").GetValue().Raw!, 1e-12);
                Assert.Equal(0.5, (double)obj.GetProperty("signal").GetValue().Raw!, 1e-12);
                Assert.Equal(0, (double)obj.GetProperty("histogram").GetValue().Raw!, 1e-12);
            }
        }

        // LOAD_INDICATOR.md, "Performance": a source built again in every iteration has the same elements, so the series
        // is computed once and reused; a source with other elements gives another series
        [Fact]
        public void Indicator_SourceBuiltInEveryIteration_IsReusedByItsElements()
        {
            var env = CreateEnvironment(out FormulaRegistry registry);
            int runs = 0;
            env.RegisterFunction(new FormuLang.NET.Core.Functions.Function("RUNS",
                [new FormuLang.NET.Core.Functions.FunctionOverload([], (_, _) => { runs++; return Value.Number(0, "0"); })]));
            registry.Indicators.Add("COUNTED", "RUNS() + SOURCE[I]");

            var context = StockContext.Create(env, OneToFive);
            var result = Executor.Create().ExecuteIterations(
                """
                $a = LOAD_INDICATOR("COUNTED", { }, MAP(CLOSE, (c) => c * 2))[I]
                $b = LOAD_INDICATOR("COUNTED", { }, MAP(CLOSE, (c) => c * 3))[I]
                $a + $b
                """, context);

            Assert.Equal(["5", "10", "15", "20", "25"], result.Values.Select(v => v.ToString()));
            Assert.Equal(2 * OneToFive.Length, runs);   // two series of N iterations, not one per caller iteration
        }

        [Fact]
        public void Indicator_MacdUsage()
        {
            const string formula = "$macd = LOAD_INDICATOR(\"MACD\", { short_period: 2, long_period: 3, signal_period: 2 })[I]\nIF(ISNULL($macd), null, ROUND($macd.histogram, 6))";
            var context = StockContext.Create(CreateEnvironment(), OneToFive);

            var result = Executor.Create().ExecuteIterations(formula, context);
            Assert.Equal(["null", "null", "null", "0", "0"], result.Values.Select(v => v.ToString()!));
        }

        [Fact]
        public void IndicatorContext_CreatedByTheHost()
        {
            var context = IndicatorContext.Create(CreateEnvironment(), OneToFive, Params(("period", 3)));
            Assert.Equal("[null, null, 2, 3, 4]", Executor.Create().ExecuteIndicator(Sma, context).ToString());
        }

        // Pattern and strategy examples (docs/standards/stock/examples/strategy_examples.md)

        private const string BullishEngulfing = """
            # In the first iteration, the previous candle is the current one
            $prev = CANDLES[MAX(I - 1, @array_indexing)]
            $curr = CANDLES[I]

            $prev_bearish = $prev.close < $prev.open
            $curr_bullish = $curr.close > $curr.open
            $covers = AND($curr.open <= $prev.close, $curr.close >= $prev.open)

            AND(I > @array_indexing, AND($prev_bearish, AND($curr_bullish, $covers)))
            """;

        private const string Crossover = """
            $fast = LOAD_INDICATOR("SMA", { period: 10 })
            $slow = LOAD_INDICATOR("SMA", { period: 40 })

            $p = MAX(I - 1, @array_indexing)
            $fast_prev = $fast[$p]
            $slow_prev = $slow[$p]
            $fast_curr = $fast[I]
            $slow_curr = $slow[I]

            # Both SMAs need a value for the previous and the current candle
            $values = [$fast_prev, $slow_prev, $fast_curr, $slow_curr]
            $ready = AND(I > @array_indexing, LEN(FILTER($values, (e) => ISNULL(e))) == 0)

            IF(NOT($ready),
               NEUTRAL,
               IF(AND($fast_prev <= $slow_prev, $fast_curr > $slow_curr),
                  BUY,
                  IF(AND($fast_prev >= $slow_prev, $fast_curr < $slow_curr), SELL, NEUTRAL)))
            """;

        private const string MacdStrength = """
            $macd = LOAD_INDICATOR("MACD")[I]

            IF(ISNULL($macd),
               NEUTRAL,
               CLAMP($macd.histogram / CLOSE[I] * 100, SELL, BUY))
            """;

        [Fact]
        public void Pattern_BullishEngulfing()
        {
            Candle[] candles =
            [
                new(10, 10.5, 7.5, 8),   // bearish
                new(7.5, 11, 7, 10.5),   // bullish, covers the previous body
                new(10.5, 11, 9, 9.5),   // bearish
                new(9.6, 10, 9, 9.8),    // bullish, does not cover
            ];

            var context = PatternContext.Create(CreateEnvironment(), candles);
            PatternResult result = Executor.Create().ExecutePattern(BullishEngulfing, context);

            Assert.True(result.Successful);
            Assert.Equal([1], result.Indices);
        }

        [Fact]
        public void Pattern_ResultThatIsNotABoolean()
        {
            var context = PatternContext.Create(CreateEnvironment(), OneToFive);
            PatternResult result = Executor.Create().ExecutePattern("I", context);

            Assert.Equal("OPERATION_ERROR", result.Error.Name);
        }

        [Fact]
        public void Strategy_MovingAverageCrossover()
        {
            // Falling prices, then rising prices: the fast SMA crosses above the slow SMA once
            double[] close = [.. Enumerable.Range(0, 50).Select(i => 100.0 - i), .. Enumerable.Range(1, 50).Select(i => 50.0 + 2 * i)];
            Candle[] candles = close.Select(Flat).ToArray();

            var context = StrategyContext.Create(CreateEnvironment(), candles);
            StrategyResult result = Executor.Create().ExecuteStrategy(Crossover, context);

            Assert.True(result.Successful, result.Error.ToString());
            Assert.Equal(candles.Length, result.Signals.Count);

            double?[] expected = ExpectedCrossover(close, 10, 40);
            Assert.Equal(expected, result.Signals);
            Assert.Single(result.Signals, s => s == StrategyContext.Buy);
        }

        [Fact]
        public void Strategy_MacdSignalStrength()
        {
            double[] close = Enumerable.Range(0, 60).Select(i => 100 + 10 * Math.Sin(i / 5.0)).ToArray();
            var context = StrategyContext.Create(CreateEnvironment(), close.Select(Flat));

            StrategyResult result = Executor.Create().ExecuteStrategy(MacdStrength, context);

            Assert.True(result.Successful, result.Error.ToString());
            Assert.All(result.Signals, s => Assert.InRange(s!.Value, -1, 1));
            Assert.All(result.Signals.Take(33), s => Assert.Equal(0, s));
            Assert.Contains(result.Signals, s => s != 0);
        }

        [Fact]
        public void Strategy_SignalsAreClamped()
        {
            var context = StrategyContext.Create(CreateEnvironment(), OneToFive);
            StrategyResult result = Executor.Create().ExecuteStrategy("I - 2 + BUY * 0 + SELL * 0 + NEUTRAL", context);

            Assert.Equal([-1, -1, 0, 1, 1], result.Signals);
        }

        // stock-1.0: IS_BULLISH, IS_BEARISH, LOAD_PATTERN and the patterns of the formula registry

        /// <summary>
        /// Open -> close: 10 -> 12, 12 -> 11, 10.5 -> 13 (bullish engulfing), 13 -> 14.
        /// </summary>
        private static readonly Candle[] Engulfing =
        [
            new(10, 12.5, 9.5, 12), new(12, 12.5, 10.8, 11), new(10.5, 13.2, 10.4, 13), new(13, 14.5, 12.9, 14),
        ];

        // docs/standards/stock/examples/strategy_examples.md, "Bullish engulfing pattern"
        private const string EngulfingPattern = """
            $prev = CANDLES[MAX(I - 1, @array_indexing)]
            $curr = CANDLES[I]
            $covers = AND($curr.open <= $prev.close, $curr.close >= $prev.open)
            AND(I > @array_indexing, AND(IS_BEARISH($prev), AND(IS_BULLISH($curr), $covers)))
            """;

        private static Env CreatePatternEnvironment()
        {
            Env env = CreateEnvironment(out FormulaRegistry registry);
            registry.Patterns.Add("ENGULFING", EngulfingPattern);
            registry.Patterns.Add("BIG_BODY", "ABS(CLOSE[I] - OPEN[I]) >= PARAMETERS.min_body", new FormulaParameter("min_body", 2));
            registry.Patterns.Add("SMA", "true"); // the same identifier as an indicator, in the other part of the registry
            registry.Patterns.Add("LOOP", "LOAD_PATTERN(\"LOOP\")[I]");
            registry.Patterns.Add("NUMBER", "1");
            return env;
        }

        private static string[] RunAll(string formula, Env env, Candle[] candles)
        {
            var context = StockContext.Create(env, candles);
            return Executor.Create().ExecuteIterations(formula, context, stopOnError: false).Results.Select(r => r.ToString()).ToArray();
        }

        [Theory]
        [InlineData("IS_BULLISH(CANDLE({ open: 10, high: 12, low: 9, close: 11 }))", "true")]
        [InlineData("IS_BULLISH(CANDLE({ open: 11, high: 12, low: 9, close: 10 }))", "false")]
        [InlineData("IS_BULLISH(CANDLE({ open: 10, high: 12, low: 9, close: 10 }))", "false")]
        [InlineData("IS_BEARISH(CANDLE({ open: 10, high: 12, low: 9, close: 11 }))", "false")]
        [InlineData("IS_BEARISH(CANDLE({ open: 11, high: 12, low: 9, close: 10 }))", "true")]
        [InlineData("IS_BEARISH(CANDLE({ open: 10, high: 12, low: 9, close: 10 }))", "false")]
        [InlineData("IS_BULLISH({ open: 10, close: 11 })", "ARGUMENT_ERROR")]
        [InlineData("IS_BEARISH(1)", "ARGUMENT_ERROR")]
        public void IsBullishAndIsBearish(string formula, string expected) => AssertResult(formula, expected);

        [Fact]
        public void IsBullish_OfTheCurrentCandle()
            => Assert.Equal(["true", "false", "true", "true"], RunAll("IS_BULLISH(CANDLES[I])", CreateEnvironment(), Engulfing));

        [Fact]
        public void LoadPattern_ReturnsTheWholeSeries()
        {
            Env env = CreatePatternEnvironment();

            Assert.Equal("[false, false, true, false]", RunAll("LOAD_PATTERN(\"ENGULFING\")", env, Engulfing)[0]);
            Assert.Equal("true", RunAll("LOAD_PATTERN(\"ENGULFING\")[I]", env, Engulfing)[2]);
            Assert.Equal("1", RunAll("LEN(FILTER(LOAD_PATTERN(\"ENGULFING\"), (e) => e))", env, Engulfing)[0]);
        }

        [Fact]
        public void LoadPattern_CompletesTheParameters()
        {
            Env env = CreatePatternEnvironment();

            Assert.Equal(["true", "false", "true", "false"], RunAll("LOAD_PATTERN(\"BIG_BODY\")[I]", env, Engulfing));
            Assert.Equal(["true", "true", "true", "true"], RunAll("LOAD_PATTERN(\"BIG_BODY\", { min_body: 1 })[I]", env, Engulfing));
        }

        [Theory]
        [InlineData("LOAD_PATTERN(\"XYZ\")", "INVALID_KEY_ERROR")]
        [InlineData("LOAD_PATTERN(\"ENGULFING\", { length: 3 })", "INVALID_KEY_ERROR")]
        [InlineData("LOAD_PATTERN(\"LOOP\")", "OPERATION_ERROR")]
        [InlineData("LOAD_PATTERN(\"NUMBER\")", "OPERATION_ERROR")]
        [InlineData("LOAD_INDICATOR(\"ENGULFING\")", "INVALID_KEY_ERROR")]
        public void LoadPattern_Errors(string formula, string expected)
            => Assert.StartsWith($"[{expected} (", RunAll(formula, CreatePatternEnvironment(), Engulfing)[0]);

        [Fact]
        public void LoadPattern_IndicatorsAndPatternsAreSeparateParts()
        {
            Env env = CreatePatternEnvironment();

            Assert.Equal("[true, true, true, true]", RunAll("LOAD_PATTERN(\"SMA\")", env, Engulfing)[0]);
            Assert.Equal("[null, 11.5, 12, 13.5]", RunAll("LOAD_INDICATOR(\"SMA\", { period: 2 })", env, Engulfing)[0]);
        }

        [Fact]
        public void LoadPattern_OutsideOfAStockContext()
            => Assert.StartsWith("[OPERATION_ERROR (", Executor.Create().Execute("LOAD_PATTERN(\"ENGULFING\")", CreatePatternEnvironment()).ToString());

        [Fact]
        public void LoadPattern_InAStrategy()
        {
            var context = StrategyContext.Create(CreatePatternEnvironment(), Engulfing);
            StrategyResult result = Executor.Create().ExecuteStrategy("IF(LOAD_PATTERN(\"ENGULFING\")[I], BUY, NEUTRAL)", context);

            Assert.Equal([0, 0, 1, 0], result.Signals);
        }

        [Fact]
        public void PatternContext_HasParameters()
        {
            var context = PatternContext.Create(CreateEnvironment(), Engulfing, parameters: Params(("min_body", 2)));
            PatternResult result = Executor.Create().ExecutePattern("ABS(CLOSE[I] - OPEN[I]) >= PARAMETERS.min_body", context);

            Assert.Equal([0, 2], result.Indices);
            Assert.Equal("{ }", Executor.Create().ExecuteIterations("PARAMETERS", PatternContext.Create(CreateEnvironment(), Engulfing)).Results[0].ToString());
        }

        [Fact]
        public void ComputePattern_FromTheHost()
        {
            var context = StockContext.Create(CreatePatternEnvironment(), Engulfing);
            Assert.Equal("[false, false, true, false]", context.ComputePattern("ENGULFING").ToString());
        }

        [Fact]
        public void Registry_IdentifiersAreUniqueWithinAPart()
        {
            var registry = new FormulaRegistry();
            registry.Indicators.Add("A", "1");
            registry.Patterns.Add("A", "true");

            Assert.Throws<InvalidOperationException>(() => registry.Patterns.Add("A", "false"));
        }

        [Fact]
        public void ChildContext_UsesTheNowOfTheCaller()
        {
            Env env = CreateEnvironment(out FormulaRegistry registry);
            registry.Indicators.Add("CLOCK", "NOW");

            var context = StockContext.Create(env, OneToFive);
            IterationExecutionResult result = Executor.Create().ExecuteIterations("ALL(LOAD_INDICATOR(\"CLOCK\"), (t) => t == NOW)", context);

            Assert.All(result.Results, r => Assert.Equal("true", r.ToString()));
        }

        // Helpers

        private static IObject Params(params (string Key, double Value)[] entries)
        {
            var obj = new Core.Primitives.Object();
            foreach (var (key, value) in entries)
                obj.InitVariable(key, Value.Number(value, ""));
            return obj;
        }

        private static double?[] ExpectedCrossover(double[] close, int fastPeriod, int slowPeriod)
        {
            double? Sma(int i, int period) => i + 1 < period ? null : close[(i - period + 1)..(i + 1)].Average();

            var signals = new double?[close.Length];

            for (int i = 0; i < close.Length; i++)
            {
                int p = Math.Max(i - 1, 0);
                double? fp = Sma(p, fastPeriod), sp = Sma(p, slowPeriod), fc = Sma(i, fastPeriod), sc = Sma(i, slowPeriod);

                if (i == 0 || fp == null || sp == null || fc == null || sc == null)
                    signals[i] = 0;
                else if (fp <= sp && fc > sc)
                    signals[i] = 1;
                else if (fp >= sp && fc < sc)
                    signals[i] = -1;
                else
                    signals[i] = 0;
            }

            return signals;
        }
    }
}
