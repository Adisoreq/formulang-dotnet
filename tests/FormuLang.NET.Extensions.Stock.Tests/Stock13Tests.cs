using FormuLang.NET.Core.Processing;
using FormuLang.NET.Core.Utils;
using FormuLang.NET.Extensions.Stock.Contexts;
using FormuLang.NET.Extensions.Stock.Registry;
using Env = FormuLang.NET.Core.Structures.Environment;

namespace FormuLang.NET.Extensions.Stock.Tests
{
    /// <summary>
    /// Stock 1.3: the examples of the documents of the standard (docs/standards/stock) that the JSON conformance tests
    /// (<c>conformance/stock/stock13.json</c>) do not cover.
    /// </summary>
    public class Stock13Tests
    {
        // docs/standards/stock/examples/indicator_examples.md, Examples 2 and 3
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
            $line = MAP($short, (s, i) => IF(OR(ISNULL(s), ISNULL($long[i])), null, s - $long[i]))
            $macd = $line[I]
            $signal = LOAD_INDICATOR("EMA", { period: PARAMETERS.signal_period }, $line)[I]
            IF(OR(ISNULL($macd), ISNULL($signal)),
               null,
               { macd: $macd, signal: $signal, histogram: $macd - $signal })
            """;

        // docs/standards/stock/examples/strategy_examples.md, Example 8 (the period of the EMA is a parameter)
        private const string MacdEma200 = """
            <@ {
              id: "macd-ema200",
              name: "MACD + EMA200",
              context: "Stock.Strategy",
              parameters: {
                emaPeriod: { type: "Number", default: 200, min: 1, description: "Period of the trend EMA" }
              },
              stock: {
                checks: {
                  aboveEma: "Price above the EMA", macdCrossUp: "MACD crosses the signal upwards", macdBelowZero: "MACD below 0",
                  belowEma: "Price below the EMA", macdCrossDown: "MACD crosses the signal downwards", macdAboveZero: "MACD above 0"
                }
              }
            } @>
            REQUIRE "stock-1.3+"

            $ema = LOAD_INDICATOR("EMA", { period: PARAMETERS.emaPeriod })
            $macd = LOAD_INDICATOR("MACD", { }, { output: "macd" })
            $signal = LOAD_INDICATOR("MACD", { }, { output: "signal" })

            $buy = {
              aboveEma: CLOSE[I] > $ema[I],
              macdCrossUp: CROSSOVER($macd, $signal),
              macdBelowZero: $macd[I] < 0
            }
            $sell = {
              belowEma: CLOSE[I] < $ema[I],
              macdCrossDown: CROSSUNDER($macd, $signal),
              macdAboveZero: $macd[I] > 0
            }

            { signal: SIGNAL_SCORE($buy, $sell), checks: MERGE($buy, $sell) }
            """;

        private static readonly double[] Closes = [5, 4, 3, 2, 3, 4, 5, 6, 7, 6, 5, 4];

        private static Env Environment()
        {
            Env env = Env.Create();
            env.SetNow(0);
            FormulaRegistry registry = env.RegisterStock().Registry;
            registry.Indicators.Add("EMA", Ema, new FormulaParameter("period", 14));
            registry.Indicators.Add("MACD", Macd,
                new FormulaParameter("short_period", 2), new FormulaParameter("long_period", 3), new FormulaParameter("signal_period", 2));
            return env;
        }

        private static string Run(string formula) => Executor.Create().Execute(formula, Environment()).Value.ToString()!;

        // Strategies without warm-up checks (docs/standards/stock/examples/strategy_examples.md)

        [Fact]
        public void Strategy_GivesASignalWithoutAnErrorDuringTheWarmUp()
        {
            StrategyContext context = StrategyContext.Create(Environment(), Stock12Tests.Candles(Closes));
            StrategyResult result = Executor.Create().ExecuteStrategy(MacdEma200, context);

            Assert.True(result.Successful, result.Error.Message);
            // The default EMA period 200 has no value on 12 candles: only the MACD conditions can be met
            Assert.All(result.Signals, s => Assert.True(s is >= -1 and <= 1, $"{s}"));
            Assert.Equal(["signal", "checks"], result.SignalObjects[0]!.Keys);
            Assert.Equal(
                "{ aboveEma: false, macdCrossUp: false, macdBelowZero: false, belowEma: false, macdCrossDown: false, macdAboveZero: false }",
                StringConverter.ValueToString(result.SignalObjects[0]!.GetProperty("checks").GetValue()));
            Assert.Equal(0, result.Signals[0]);
        }

        [Fact]
        public void Strategy_WeightsTheConditionsOfBothSidesWithOneObject()
        {
            const string formula = """
                $ema = LOAD_INDICATOR("EMA", { period: 3 })
                $buy = { aboveEma: CLOSE[I] > $ema[I], rising: CLOSE[I] > GET(CLOSE, I - 1, null) }
                $sell = { belowEma: CLOSE[I] < $ema[I], falling: CLOSE[I] < GET(CLOSE, I - 1, null) }
                SIGNAL_SCORE($buy, $sell, { aboveEma: 2, belowEma: 2 })
                """;

            StrategyContext context = StrategyContext.Create(Environment(), Stock12Tests.Candles([1, 2, 3, 4, 3, 2]));
            StrategyResult result = Executor.Create().ExecuteStrategy(formula, context);

            Assert.True(result.Successful, result.Error.Message);
            // EMA(3) = [null, null, 2, 3, 3, 2.5]
            Assert.Equal([0, 1.0 / 3, 1, 1, -1.0 / 3, -1], result.Signals);
        }

        [Fact]
        public void Strategy_AppliesAFilterToTheSignal()
        {
            const string formula = "$isVolatile = WINDOW(CLOSE, 3, AVG) > 2\nIF($isVolatile, SIGNAL_SCORE([CLOSE[I] > 2], [CLOSE[I] < 2]), NEUTRAL)";
            StrategyContext context = StrategyContext.Create(Environment(), Stock12Tests.Candles([1, 2, 3, 4]));
            Assert.Equal([0, 0, 0, 1], Executor.Create().ExecuteStrategy(formula, context).Signals);
        }

        // SCORE.md and SIGNAL_SCORE.md

        [Theory]
        [InlineData("SIGNAL_SCORE({ a: true, b: false }, { c: true, d: false }, { a: 3, d: 3 })", "0.5")]
        [InlineData("SIGNAL_SCORE({ a: true }, { b: 1 }, { a: 1 })", "ARGUMENT_ERROR")]
        [InlineData("SIGNAL_SCORE({ a: true }, { b: false }, { a: \"1\" })", "ARGUMENT_ERROR")]
        [InlineData("SIGNAL_SCORE([true, 1], [])", "ARGUMENT_ERROR")]
        [InlineData("SIGNAL_SCORE([true, null], [null])", "0.5")]
        [InlineData("SIGNAL_SCORE({ a: true })", "ARGUMENT_ERROR")]
        [InlineData("SIGNAL_SCORE({ a: ERROR(\"x\") }, { })", "[CUSTOM_ERROR (1)] x")]
        [InlineData("SIGNAL_SCORE({ a: true }, { b: false }, { a: 0 })", "0")]
        [InlineData("SCORE([null, null])", "0")]
        [InlineData("SCORE({ a: null }, { a: 2 })", "0")]
        [InlineData("SCORE([true, null], [1, 3])", "0.25")]
        public void Scores(string formula, string expected)
        {
            string actual = Run(formula);

            if (expected.EndsWith("_ERROR"))
                Assert.StartsWith($"[{expected} (", actual);
            else
                Assert.Equal(expected, actual);
        }

        [Fact]
        public void SignalScore_IsAPartOfStock13()
        {
            Assert.Equal("stock-1.3", StockStandard.Identifier);
            Assert.Equal("core-1.3+", StockStandard.RequiredCore);
            Assert.StartsWith("[SUCCESS", Run("REQUIRE \"stock-1.3+\""));
        }

        // Indicators with missing values (docs/standards/stock/functions/LAG.md)

        [Fact]
        public void Indicator_GivesNullInsteadOfAnErrorForTheChangeFromThePreviousClose()
        {
            StockContext context = StockContext.Create(Environment(), Stock12Tests.Candles([1, 2, 3]));
            Assert.Equal(["null", "1", "1"],
                Executor.Create().ExecuteIterations("CLOSE[I] - LAG(CLOSE)", context, stopOnError: false).Results.Select(r => r.ToString()));
        }

        [Fact]
        public void KeyOfNull_IsComparedAsFalseInAStockContext()
        {
            StockContext context = StockContext.Create(Environment(), Stock12Tests.Candles([1]), new StockContextOptions { Timeframe = "15m" });
            Assert.Equal("false", Executor.Create().ExecuteIterations("null.close > 1", context, stopOnError: false).Results[0].ToString());
        }
    }
}
