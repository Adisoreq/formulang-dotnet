using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Core.Utils;
using FormuLang.NET.Extensions.Stock.Contexts;
using FormuLang.NET.Extensions.Stock.Registry;
using Env = FormuLang.NET.Core.Structures.Environment;
using FlObject = FormuLang.NET.Core.Primitives.Object;

namespace FormuLang.NET.Extensions.Stock.Tests
{
    /// <summary>
    /// Stock 1.2: the examples of the documents of the standard (docs/standards/stock) that the JSON conformance tests do
    /// not cover, and the API of the library for the other timeframes, the signal objects and the resumed iterations.
    /// </summary>
    public class Stock12Tests
    {
        // docs/standards/stock/examples/indicator_examples.md, "Example 4: Relative Strength Index (RSI)"
        internal const string Rsi = """
            <@ {
              id: "RSI",
              name: "Relative Strength Index",
              context: "Stock.Indicator",
              result: ["Number", "Null"],
              parameters: { period: { type: "Number", default: 14, min: 1, description: "Number of changes in the averages" } },
              stock: { category: "Momentum", outputs: { value: { name: "RSI" } } }
            } @>
            REQUIRE "stock-1.2+"

            $p = PARAMETERS.period
            $n = I - @array_indexing              # number of changes up to the current candle
            $change = IF($n == 0, 0, SOURCE[I] - SOURCE[I - 1])
            $gain = MAX($change, 0)
            $loss = MAX(-$change, 0)

            # Warm-up: the sums of the first `period` changes
            $sumGain = RECALL("sumGain", 0) + $gain
            $sumLoss = RECALL("sumLoss", 0) + $loss
            STORE("sumGain", $sumGain)
            STORE("sumLoss", $sumLoss)

            # Wilder's averages: the mean of the first `period` changes, then a smoothing of the previous average
            $avgGain = IF($n < $p, null, IF($n == $p, $sumGain / $p, (RECALL("avgGain") * ($p - 1) + $gain) / $p))
            $avgLoss = IF($n < $p, null, IF($n == $p, $sumLoss / $p, (RECALL("avgLoss") * ($p - 1) + $loss) / $p))
            STORE("avgGain", $avgGain)
            STORE("avgLoss", $avgLoss)

            IF(ISNULL($avgGain), null, IF($avgLoss == 0, 100, 100 - 100 / (1 + $avgGain / $avgLoss)))
            """;

        private const string Sma2 = "WINDOW(SOURCE, 2, AVG)";

        /// <summary>
        /// Candles with the closes, <paramref name="length"/> milliseconds long, from the time 0; open is the close minus 0.5.
        /// </summary>
        internal static Candle[] Candles(IEnumerable<double> closes, double length = 900_000)
            => closes.Select((close, i) => new Candle(close - 0.5, close + 0.5, close - 1, close, null, i * length)).ToArray();

        private static Env Environment()
        {
            Env env = Env.Create();
            env.SetNow(0);
            FormulaRegistry registry = env.RegisterStock().Registry;
            registry.AddFormula(Rsi);
            registry.Indicators.Add("SMA2", Sma2);
            registry.Patterns.Add("UP", "CANDLES[I].close > CANDLES[I].open");
            return env;
        }

        /// <summary>
        /// 8 candles of 15 minutes; the timeframe 1h has 2 candles with the closes 4 and 8.
        /// </summary>
        private static StockContextOptions Hourly() => new()
        {
            Timeframe = "15m",
            Instrument = "EURUSD",
            TimeframeLength = 900_000,
            Timeframes = new Dictionary<string, TimeframeCandles> { ["1h"] = new(3_600_000, Candles([4, 8], 3_600_000)) },
        };

        private static string[] Iterate(string formula, StockContext context, Executor? executor = null)
            => (executor ?? Executor.Create()).ExecuteIterations(formula, context, stopOnError: false).Results.Select(r => r.ToString()).ToArray();

        private static FlObject Parameters(params (string Key, double Value)[] values)
        {
            var obj = new FlObject();
            foreach (var (key, value) in values)
                obj.InitVariable(key, Value.Number(value));
            return obj;
        }

        // RSI with the memory

        [Fact]
        public void Rsi_IsReadFromItsMetadataIntoTheRegistry()
        {
            FormulaDefinition definition = FormulaDefinition.FromFormula(Rsi);

            Assert.Equal("RSI", definition.Id);
            Assert.Equal(["period=14"], definition.Parameters.Select(p => $"{p.Name}={StringConverter.ValueToString(p.DefaultValue)}"));
        }

        [Fact]
        public void Rsi_GivesTheValuesOfTheExample()
        {
            StockContext context = StockContext.Create(Environment(), Candles([1, 2, 1, 3]));
            Assert.Equal("[null, null, 50, 83.33333333333333]", StringConverter.ValueToString(context.ComputeIndicator("RSI", Parameters(("period", 2)))));
        }

        [Fact]
        public void Rsi_HasNoWarningsInAnIndicatorContext()
        {
            PreparedFormula prepared = Executor.Create().Prepare(Rsi, IndicatorContext.Create(Environment(), Candles([1])));
            Assert.Empty(prepared.Warnings);

            PreparedFormula inStrategy = Executor.Create().Prepare(Rsi, StrategyContext.Create(Environment(), Candles([1])));
            Assert.Equal(["The formula is written for the context \"Stock.Indicator\", but is executed in \"Stock.Strategy\"."],
                inStrategy.Warnings.Select(w => w.Message));
        }

        [Fact]
        public void StockMetadata_IsChecked()
        {
            PreparedFormula prepared = Executor.Create().Prepare(
                "<@ { stock: { category: 1, outputs: { a: { name: 2 } }, checks: { b: true } } } @>\n1",
                StockContext.Create(Environment(), Candles([1])));

            Assert.Equal(
            [
                "Metadata key \"stock.category\" must be of type Text.",
                "Metadata key \"stock.outputs.a.name\" must be of type Text.",
                "Metadata key \"stock.checks.b\" must be of type Text.",
            ], prepared.Warnings.Select(w => w.Message));
        }

        // Strategies (docs/standards/stock/examples/strategy_examples.md)

        [Fact]
        public void Strategy_CompletesParametersWithTheDefaultValuesOfTheMetadata()
        {
            const string formula = """
                <@ {
                  id: "sma-cross",
                  context: "Stock.Strategy",
                  parameters: { fast: { type: "Number", default: 1, min: 1 }, slow: { type: "Number", default: 3, min: 1 } }
                } @>
                REQUIRE "stock-1.2+"
                $fast = WINDOW(CLOSE, PARAMETERS.fast, AVG)
                $slow = WINDOW(CLOSE, PARAMETERS.slow, AVG)
                IF(CROSSOVER(MAP(CLOSE, (c, i) => IF(i < PARAMETERS.slow - 1, null, c)), 3), BUY, NEUTRAL)
                """;

            StrategyContext context = StrategyContext.Create(Environment(), Candles([5, 4, 3, 2, 3, 4, 5, 6]), new StockContextOptions(), Parameters(("fast", 2)));
            StrategyResult result = Executor.Create().ExecuteStrategy(formula, context);

            Assert.True(result.Successful, result.Error.Message);
            Assert.Equal("{ fast: 2, slow: 3 }", StringConverter.ValueToString(context.Parameters));
            Assert.Equal([0, 0, 0, 0, 0, 1, 0, 0], result.Signals);
        }

        [Fact]
        public void Strategy_ReturnsTheSignalObjectsWithTheChecks()
        {
            const string formula = "$buy = { up: IS_BULLISH(CANDLES[I]), rising: RISING(CLOSE, 1) }\n{ signal: SCORE($buy), checks: $buy, reason: \"r\", stop: LOWEST(LOW, 2) }";
            StrategyResult result = Executor.Create().ExecuteStrategy(formula, StrategyContext.Create(Environment(), Candles([1, 2, 3])));

            Assert.Equal([0.5, 1, 1], result.Signals);
            Assert.Equal(
            [
                "{ signal: 0.5, checks: { up: true, rising: false }, reason: \"r\", stop: null }",
                "{ signal: 1, checks: { up: true, rising: true }, reason: \"r\", stop: 0 }",
                "{ signal: 1, checks: { up: true, rising: true }, reason: \"r\", stop: 1 }",
            ], result.SignalObjects.Select(o => o == null ? "-" : StringConverter.ValueToString(Value.Object(o, ""))));
        }

        [Fact]
        public void Strategy_ClampsTheSignalOfASignalObject()
            => Assert.Equal([1], Executor.Create().ExecuteStrategy("{ signal: 5 }", StrategyContext.Create(Environment(), Candles([1]))).Signals);

        [Fact]
        public void Strategy_ReportsAResultThatIsNeitherANumberNorASignalObject()
            => Assert.Equal("Iteration 0 of the strategy returned a value that is neither a Number nor a signal object.",
                Executor.Create().ExecuteStrategy("\"x\"", StrategyContext.Create(Environment(), Candles([1]))).Error.Message);

        [Fact]
        public void Strategy_UsesTheTrendOfAnotherTimeframe()
        {
            const string formula = """
                REQUIRE "stock-1.2+"
                $trend = LOAD_INDICATOR("SMA2", { }, { timeframe: "1h" })
                $hourly = LOAD_CANDLES("1h")
                $uptrend = READY($trend, 1) && $hourly[I].close > $trend[I]
                IF($uptrend && CROSSOVER(CLOSE, 7), BUY, NEUTRAL)
                """;

            StrategyContext context = StrategyContext.Create(Environment(), Candles([1, 2, 3, 4, 5, 6, 7, 8]), Hourly());
            Assert.Equal([0, 0, 0, 0, 0, 0, 0, 1], Executor.Create().ExecuteStrategy(formula, context).Signals);
        }

        // Other timeframes (docs/standards/stock/README.md, "Other timeframes")

        private static StockContext HourlyContext() => StockContext.Create(Environment(), Candles([1, 2, 3, 4, 5, 6, 7, 8]), Hourly());

        [Fact]
        public void OtherTimeframe_IndicatorWithASource()
            => Assert.Equal(["null", "null", "null", "null", "null", "null", "null", "1.5"],
                Iterate("LOAD_INDICATOR(\"SMA2\", { }, { timeframe: \"1h\", source: [1, 2] })[I]", HourlyContext()));

        [Fact]
        public void OtherTimeframe_TheMainTimeframeIsAlignedWithItself()
            => Assert.Equal(Enumerable.Repeat("true", 8),
                Iterate("LOAD_CANDLES(\"15m\")[I].close == CLOSE[I] && LEN(LOAD_CANDLES(\"15m\", { align: false })) == I + 1", HourlyContext()));

        [Fact]
        public void OtherTimeframe_NativeSeriesOfAPattern()
            => Assert.Equal(["[]", "[]", "[]", "[true]", "[true]", "[true]", "[true]", "[true, true]"],
                Iterate("LOAD_PATTERN(\"UP\", { }, { timeframe: \"1h\", align: false })", HourlyContext()));

        [Fact]
        public void OtherTimeframe_RejectsASourceOfAWrongLength()
            => Assert.Equal("[OPERATION_ERROR (3)] The source has 3 elements, but there are 2 candles of the timeframe \"1h\".",
                Iterate("LOAD_INDICATOR(\"SMA2\", { }, { timeframe: \"1h\", source: [1, 2, 3] })", HourlyContext())[0]);

        [Fact]
        public void OtherTimeframe_RejectsAnOptionOfAWrongType()
            => Assert.Equal("[ARGUMENT_ERROR (6)] The option \"align\" of \"LOAD_PATTERN\" must be of type Boolean.",
                Iterate("LOAD_PATTERN(\"UP\", { }, { align: 1 })", HourlyContext())[0]);

        [Fact]
        public void OtherTimeframe_IsAskedTheFirstTimeItIsUsed()
        {
            var asked = new List<string>();
            StockContext context = StockContext.Create(Environment(), Candles([1, 2, 3, 4]), new StockContextOptions
            {
                TimeframeLength = 900_000,
                TimeframeProvider = name =>
                {
                    asked.Add(name);
                    return name == "1h" ? new TimeframeCandles(3_600_000, Candles([4], 3_600_000)) : null;
                },
            });

            Assert.Equal(["10", "10", "10", "11"],
                Iterate("LEN(LOAD_CANDLES(\"1h\", { align: false })) + IFERR(LEN(LOAD_CANDLES(\"1d\")), 10)", context));
            Assert.Equal(["1d", "1h"], asked.Order());
        }

        [Fact]
        public void OtherTimeframe_NeedsTheLengthOfTheMainTimeframe()
            => Assert.Throws<ArgumentException>(() => StockContext.Create(Environment(), Candles([1]),
                new StockContextOptions { Timeframes = new Dictionary<string, TimeframeCandles>() }));

        [Fact]
        public void TimeframeAndInstrument_AreConstants()
            => Assert.Equal(Enumerable.Repeat("[\"15m\", \"EURUSD\"]", 8), Iterate("[TIMEFRAME, INSTRUMENT]", HourlyContext()));

        // Resumed iterations (docs/core/context.md, "Resumed iterations")

        [Fact]
        public void ResumedIterations_GiveTheSameResultsAsAllTheIterations()
        {
            const string formula = "$n = RECALL(\"n\", 0) + 1\nSTORE(\"n\", $n)\n[$n, LOAD_INDICATOR(\"SMA2\")[I], LEN(HISTORY)]";
            double[] closes = [1, 2, 3, 4, 5, 6];
            var executor = Executor.Create();
            string[] all = Iterate(formula, StockContext.Create(Environment(), Candles(closes)), executor);

            StockContext live = StockContext.Create(Environment(), Candles(closes[..4]));
            var first = executor.ExecuteIterations(formula, live).Results.Select(r => r.ToString());
            live.AppendCandles(Candles(closes)[4..]);
            var rest = executor.ResumeIterations(formula, live).Results.Select(r => r.ToString());

            Assert.Equal(all, first.Concat(rest));
            Assert.Equal(6, live.Count);
        }

        [Fact]
        public void StockContext_CannotBeExtendedWithoutCandles()
            => Assert.Throws<InvalidOperationException>(() => StockContext.Create(Environment(), Candles([1])).Extend(2));

        // Series functions (docs/standards/stock/functions)

        [Theory]
        [InlineData("CROSSOVER([1, 2, 4, 5], [3, 2, 3, 3])", "false|false|true|false")]
        [InlineData("CROSSUNDER([5, 4, 2, 1], 3)", "false|false|true|false")]
        [InlineData("HIGHEST([5, 7, 6, 9, 8], 3, 2)", "null|null|null|null|7")]
        [InlineData("HIGHEST([null, 2, null, 1, null], 3)", "null|null|2|2|1")]
        [InlineData("LOWEST([5, 3, 6, 4, 8], 3, 1)", "null|null|null|3|3")]
        [InlineData("FALLING([5, 4, 3, 3, 2], 2)", "false|false|true|false|false")]
        [InlineData("READY([null, null, 3, 4, 5], 2)", "false|false|false|true|true")]
        [InlineData("VALUE_WHEN([true, false, true, false], [10, 11, 12, 13], 1)", "null|null|10|10")]
        [InlineData("VALUE_WHEN([true, false, true, false], [10, 11, 12, 13], -1)", "OPERATION_ERROR|OPERATION_ERROR|OPERATION_ERROR|OPERATION_ERROR")]
        [InlineData("CROSSOVER([1, 2, \"x\", 4], 3)", "false|false|OPERATION_ERROR|OPERATION_ERROR")]
        public void SeriesFunctions(string formula, string expected)
        {
            string[] wanted = expected.Split('|');
            string[] results = Iterate(formula, StockContext.Create(Environment(), Candles(Enumerable.Range(1, wanted.Length).Select(i => (double)i))));

            for (int i = 0; i < wanted.Length; i++)
            {
                if (wanted[i].EndsWith("_ERROR"))
                    Assert.StartsWith($"[{wanted[i]} (", results[i]);
                else
                    Assert.Equal(wanted[i], results[i]);
            }
        }

        [Fact]
        public void SeriesFunctions_DoNotDependOnArrayIndexing()
            => Assert.Equal(["null", "2", "3"], Iterate("@array_indexing = 1\nHIGHEST(CLOSE, 2)", StockContext.Create(Environment(), Candles([1, 2, 3]))));

        // Formula registry from the metadata

        [Fact]
        public void Registry_AddsAPatternByTheContextOfItsMetadata()
        {
            FormulaRegistry registry = Env.Create().RegisterStock().Registry;
            FormulaDefinition definition = registry.AddFormula("<@ { id: \"P\", context: \"Stock.Pattern\", parameters: { k: { } } } @>\ntrue");

            Assert.Same(definition, registry.Patterns.Get("P"));
            Assert.Equal(["null"], definition.Parameters.Select(p => StringConverter.ValueToString(p.DefaultValue)));
        }

        [Theory]
        [InlineData("<@ { context: \"Stock.Indicator\" } @>\n1", "The metadata of the formula has no Text \"id\".")]
        [InlineData("<@ { id: \"X\", context: \"Stock.Strategy\" } @>\n1", "The \"context\" of the metadata must be \"Stock.Indicator\" or \"Stock.Pattern\".")]
        [InlineData("1", "The metadata of the formula has no Text \"id\".")]
        public void Registry_RejectsAFormulaWithoutAValidDefinition(string formula, string message)
        {
            var exception = Assert.Throws<ArgumentException>(() => Env.Create().RegisterStock().Registry.AddFormula(formula));
            Assert.StartsWith(message, exception.Message);
        }

        [Fact]
        public void Registry_IdentifiersMayContainAHyphen()
        {
            Env env = Env.Create();
            FormulaRegistry registry = env.RegisterStock().Registry;
            registry.Indicators.Add("ema-fast", "SOURCE[I]");

            Assert.Equal("[1, 2, 3]", StringConverter.ValueToString(StockContext.Create(env, Candles([1, 2, 3])).ComputeIndicator("ema-fast")));
            Assert.Throws<ArgumentException>(() => registry.Indicators.Add("-x", "1"));
        }
    }
}
