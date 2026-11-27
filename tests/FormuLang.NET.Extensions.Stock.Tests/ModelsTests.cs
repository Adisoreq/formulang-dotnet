using System.Text.Json;
using FormuLang.NET.Core.Model;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Utils;
using FormuLang.NET.Extensions.Stock.Contexts;
using FormuLang.NET.Extensions.Stock.Models;
using Env = FormuLang.NET.Core.Structures.Environment;

namespace FormuLang.NET.Extensions.Stock.Tests
{
    /// <summary>
    /// Formula models (FormuLang docs/implementation/formula_models.md): the API of the library that the JSON conformance
    /// cases (conformance/stock/models.json) do not cover.
    /// </summary>
    public class ModelsTests
    {
        private const string Rsi = """
            <@ {
              id: "RSI", name: "Relative Strength Index", context: "Stock.Indicator",
              parameters: { period: { type: "Number", default: 2, min: 1 } },
              stock: { category: "Momentum", outputs: { value: { name: "RSI" } } }
            } @>
            $p = PARAMETERS.period
            $n = I - @array_indexing
            $change = IF($n == 0, 0, SOURCE[I] - SOURCE[I - 1])
            $sumGain = RECALL("sumGain", 0) + MAX($change, 0)
            $sumLoss = RECALL("sumLoss", 0) + MAX(-$change, 0)
            STORE("sumGain", $sumGain)
            STORE("sumLoss", $sumLoss)
            $avgGain = IF($n < $p, null, IF($n == $p, $sumGain / $p, (RECALL("avgGain") * ($p - 1) + MAX($change, 0)) / $p))
            $avgLoss = IF($n < $p, null, IF($n == $p, $sumLoss / $p, (RECALL("avgLoss") * ($p - 1) + MAX(-$change, 0)) / $p))
            STORE("avgGain", $avgGain)
            STORE("avgLoss", $avgLoss)
            IF(ISNULL($avgGain), null, IF($avgLoss == 0, 100, 100 - 100 / (1 + $avgGain / $avgLoss)))
            """;

        private const string Strategy = """
            <@ {
              id: "rsi-cross", context: "Stock.Strategy",
              parameters: { level: { type: "Number", default: 50, min: 0, max: 100 }, side: { type: "Text", default: "long", options: ["long", "short"] } },
              stock: { checks: { cross: "RSI crosses the level" } }
            } @>
            $rsi = LOAD_INDICATOR("RSI", { period: 2 })
            $cross = CROSSOVER($rsi, PARAMETERS.level)
            IF($cross, { signal: IF(PARAMETERS.side == "long", BUY, SELL), checks: { cross: $cross }, reason: "cross", stop: LOW[I] }, NEUTRAL)
            """;

        private static readonly double[] Closes = [1, 2, 1, 3, 2, 4, 3, 5];

        private static Candle[] Candles(IEnumerable<double> values)
            => values.Select((close, i) => new Candle(close, close + 1, close - 1, close, null, i * 60_000)).ToArray();

        private static Env Environment()
        {
            Env env = Env.Create();
            env.SetNow(0);
            env.RegisterStock().Registry.AddDefinition(Definition.FromFormula(Rsi));
            return env;
        }

        private static StrategyDefinition StrategyDefinition => (StrategyDefinition)Definition.FromFormula(Strategy);

        // Definitions

        [Fact]
        public void Definitions_AreReadFromTheMetadata()
        {
            var rsi = Assert.IsType<IndicatorDefinition>(Definition.FromFormula(Rsi));
            Assert.Equal("Relative Strength Index", rsi.Name);
            Assert.Equal("Momentum", rsi.Category);
            Assert.Equal([new OutputDescription("value", "RSI", null)], rsi.Outputs!);

            StrategyDefinition strategy = StrategyDefinition;
            Assert.Equal([new CheckDescription("cross", "RSI crosses the level")], strategy.Checks!);
            Assert.Equal(["level:Number:50", "side:Text:long"],
                strategy.Parameters.Select(p => $"{p.Key}:{p.Type}:{StringConverter.ValueToString(p.Default!)}"));
        }

        [Theory]
        [InlineData("<@ { id: \"x\", context: \"Stock.Pattern\" } @>\n\"unclosed", InvalidDefinitionReason.Error)]
        [InlineData("<@ { id: \"x\", context: \"Stock.Pattern\", stock: { category: 1 } } @>\ntrue", InvalidDefinitionReason.MetadataType)]
        [InlineData("<@ { id: \"x-\", context: \"Stock\" } @>\ntrue", InvalidDefinitionReason.Context)]
        [InlineData("<@ { context: \"Stock.Pattern\" } @>\ntrue", InvalidDefinitionReason.Id)]
        public void Definitions_GiveTheReasonOfAnInvalidFormula(string formula, InvalidDefinitionReason reason)
        {
            Assert.Equal(reason, Assert.Throws<InvalidDefinitionException>(() => Definition.FromFormula(formula)).Reason);
            Assert.False(Definition.TryFromFormula(formula, out _, out var problem));
            Assert.Equal(reason, problem!.Reason);
        }

        [Fact]
        public void Definitions_GoIntoTheFormulaRegistry()
        {
            StockContext context = StockContext.Create(Environment(), Candles(Closes));

            Assert.Equal("[null, null, 50, 83.33333333333333, 50, 80.76923076923077, 50, 80.18867924528303]",
                StringConverter.ValueToString(context.ComputeIndicator("RSI")));
            Assert.Throws<ArgumentException>(() => Env.Create().RegisterStock().Registry.AddDefinition(StrategyDefinition));
        }

        // Instances

        [Fact]
        public void Instances_AcceptDotNetValuesAndCompleteTheDefaults()
        {
            StrategyInstance instance = StrategyDefinition.CreateInstance(new Dictionary<string, object?> { ["level"] = 60 });

            Assert.Equal("{ level: 60, side: \"long\" }", StringConverter.ValueToString(instance.ParametersValue));
            Assert.Equal("rsi-cross|{ level: 60, side: \"long\" }", instance.Key);
        }

        [Fact]
        public void Instances_ReportEveryProblemOfTheValues()
        {
            ParameterNormalization normalization = StrategyDefinition.Normalize(new Dictionary<string, object?> { ["level"] = 101, ["side"] = "both", ["x"] = true });

            Assert.Equal([(ParameterProblemCode.Unknown, "x"), (ParameterProblemCode.Max, "level"), (ParameterProblemCode.Option, "side")],
                normalization.Problems.Select(p => (p.Code, p.Key)));

            var exception = Assert.Throws<ParameterException>(() => StrategyDefinition.CreateInstance(new Dictionary<string, object?> { ["level"] = "high" }));
            Assert.Equal(ParameterProblemCode.Type, Assert.Single(exception.Problems).Code);

            // A value that is not a FormuLang value is a TYPE problem
            Assert.Equal(ParameterProblemCode.Type,
                Assert.Single(StrategyDefinition.Normalize(new Dictionary<string, object?> { ["level"] = new object() }).Problems).Code);
        }

        [Fact]
        public void Instances_AreSerializedAndReadBack()
        {
            StrategyDefinition definition = StrategyDefinition;
            StrategyInstance instance = definition.CreateInstance(new Dictionary<string, object?> { ["side"] = "short" });

            string json = JsonSerializer.Serialize(instance.ToJson(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal("{\"id\":\"rsi-cross\",\"parameters\":{\"level\":50,\"side\":\"short\"}}", json);

            using JsonDocument document = JsonDocument.Parse(json);
            Assert.Equal(instance.Key, definition.InstanceFromJson(document.RootElement).Key);
            Assert.Equal(instance.Key, definition.InstanceFromJson(instance.ToJson()).Key);
            Assert.Throws<ArgumentException>(() => definition.InstanceFromJson(instance.ToJson() with { Id = "other" }));
        }

        // Runs

        [Fact]
        public void Runs_GiveTheSignalsOfAStrategyWithTheirChecksAndExtraKeys()
        {
            StrategySignals run = StrategyDefinition.CreateInstance(new Dictionary<string, object?> { ["level"] = 60 })
                .Compute(Environment(), new Chart(Candles(Closes)));

            Assert.True(run.Successful, run.Error.Message);
            Assert.Equal([(3, 180_000.0, 1.0), (5, 300_000.0, 1.0), (7, 420_000.0, 1.0)], run.Signals.Select(s => (s.Index, s.Time!.Value, s.Signal)));
            Assert.All(run.Signals, s =>
            {
                Assert.Equal("cross", s.Reason);
                Assert.Equal(new Dictionary<string, bool> { ["cross"] = true }, s.Checks);
            });
            Assert.Equal([2.0, 3.0, 4.0], run.Signals.Select(s => s.Number("stop")!.Value));
            Assert.Equal(Closes.Length, run.Results.Count);
        }

        [Fact]
        public void Runs_GiveTheErrorOfAnIteration()
        {
            var pattern = (PatternDefinition)Definition.FromFormula("<@ { id: \"p\", context: \"Stock.Pattern\" } @>\nIF(I == 1, 1, true)");
            PatternSeries run = pattern.CreateInstance().Compute(Environment(), new Chart(Candles([1, 2, 3])));

            Assert.False(run.Successful);
            Assert.Equal("Iteration 1 of the pattern returned a value that is not a Boolean.", run.Error.Message);
        }

        [Fact]
        public void Runs_ContinueLiveWithTheSameResultsAsANewRun()
        {
            Env env = Environment();
            IndicatorInstance instance = ((IndicatorDefinition)Definition.FromFormula(Rsi)).CreateInstance();
            IndicatorSeries all = instance.Compute(env, new Chart(Candles(Closes)));

            LiveIndicatorRun live = instance.Live(env, new Chart(Candles(Closes[..5])));
            IndicatorSeries first = live.Start();
            IndicatorSeries rest = live.Append(Candles(Closes)[5..]);

            Assert.Equal(5, rest.From);
            Assert.Equal(all["value"], first["value"].Concat(rest["value"]));
        }

        [Fact]
        public void Runs_RestoreASavedStateInANewLiveRun()
        {
            Env env = Environment();
            IndicatorInstance instance = ((IndicatorDefinition)Definition.FromFormula(Rsi)).CreateInstance();
            IReadOnlyList<double?> all = instance.Compute(env, new Chart(Candles(Closes)))["value"];

            LiveIndicatorRun before = instance.Live(env, new Chart(Candles(Closes[..6])));
            before.Start();
            Assert.Equal(6, before.SaveState().CompletedIterations);

            LiveIndicatorRun after = instance.Live(env, new Chart(Candles(Closes[..6])));
            after.RestoreState(before.SaveState());
            Assert.Equal(all.Skip(6), after.Append(Candles(Closes)[6..])["value"]);
        }

        [Fact]
        public void Runs_OfALiveStrategyGiveOnlyTheSignalsOfTheNewCandles()
        {
            Env env = Environment();
            StrategyInstance instance = StrategyDefinition.CreateInstance(new Dictionary<string, object?> { ["level"] = 60 });

            LiveStrategyRun live = instance.Live(env, new Chart(Candles(Closes[..6])));
            Assert.Equal([3, 5], live.Start().Signals.Select(s => s.Index));
            Assert.Equal([7], live.Append(Candles(Closes)[6..]).Signals.Select(s => s.Index));
        }

        [Fact]
        public void IdentifiersWithAHyphen_AreDefinitionsOfTheRegistry()
        {
            Env env = Env.Create();
            env.SetNow(0);
            env.RegisterStock().Registry.AddDefinition(Definition.FromFormula("<@ { id: \"sma-2\", context: \"Stock.Indicator\" } @>\nWINDOW(SOURCE, 2, AVG)"));

            Assert.Equal("[null, 1.5, 2.5]", StringConverter.ValueToString(StockContext.Create(env, Candles([1, 2, 3])).ComputeIndicator("sma-2")));
        }
    }
}
