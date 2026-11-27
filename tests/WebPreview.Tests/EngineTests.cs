namespace WebPreview.Tests
{
    /// <summary>Execution of the formulas of every context type.</summary>
    public class EngineTests
    {
        private static readonly IReadOnlyList<ChartCandle> Chart = Candles.Generate(120, 20251);

        private static IndicatorSetup SetupOf(string id)
        {
            IndicatorConfig config = Examples.Indicators.First(i => i.Id == id);
            return new IndicatorSetup(IndicatorDraft.Of(config), config.Formula);
        }

        [Fact]
        public void DefaultRunReturnsValueAndVariables()
        {
            DefaultRun run = Engine.RunDefault("$r := 2\n$r * 3", Engine.CreateEnvironment());

            Assert.True(run.Execution.Successful);
            Assert.Equal(6.0, run.Execution.Value.Raw);
            Assert.Contains(run.Variables, v => v.Name == "$r" && v.Value == "2");
        }

        [Fact]
        public void DiagnoseReportsEveryValidationError()
        {
            Diagnostics diagnostics = Engine.Diagnose("$x = (1 + 2\n$y = \"not closed", Engine.CreateEnvironment());
            Assert.True(diagnostics.ValidationErrors.Count >= 2);
            Assert.Empty(diagnostics.Sequences);
        }

        [Fact]
        public void DiagnoseGivesCanonicalSequences()
        {
            Diagnostics diagnostics = Engine.Diagnose("$x := 1 + 2", Engine.CreateEnvironment());
            Assert.Empty(diagnostics.ValidationErrors);
            Assert.Single(diagnostics.Sequences);
        }

        [Fact]
        public void IterationsRunTheRequestedNumberOfTimes()
        {
            IterationRun run = Engine.RunIterations("IF(ISNULL(PREV), 1, PREV * 2)", 5, Engine.CreateEnvironment());
            Assert.Equal(5, run.Results.Count);
            Assert.Equal(16.0, run.Results[^1].Value.Raw);
        }

        [Fact]
        public void SmaHasNoValueUntilTheWindowIsComplete()
        {
            IndicatorRun run = Engine.RunIndicator(SetupOf("sma"), Chart, Engine.CreateEnvironment());

            Assert.Null(run.Error);
            IReadOnlyList<double?> sma = run.Series["sma"];
            Assert.Equal(Chart.Count, sma.Count);
            Assert.All(sma.Take(19), v => Assert.Null(v));

            double expected = Chart.Skip(10).Take(20).Average(c => c.Close);
            Assert.Equal(expected, sma[29]!.Value, precision: 9);
        }

        [Fact]
        public void IndicatorErrorIsReported()
        {
            IndicatorSetup setup = SetupOf("sma") with { Formula = "1 / \"x\"" };
            IndicatorRun run = Engine.RunIndicator(setup, Chart, Engine.CreateEnvironment());
            Assert.NotNull(run.Error);
        }

        private static PatternSetup PatternOf(string id)
        {
            PatternConfig config = Examples.Patterns.First(p => p.Id == id);
            return new PatternSetup(PatternDraft.Of(config), config.Formula);
        }

        [Theory]
        [InlineData("bullish-engulfing")]
        [InlineData("bearish-engulfing")]
        [InlineData("hammer")]
        [InlineData("shooting-star")]
        [InlineData("three-soldiers")]
        public void PatternExamplesRunWithoutErrors(string id)
        {
            PatternRun run = Engine.RunPattern(PatternOf(id), Chart, Engine.CreateEnvironment());
            Assert.Null(run.Error);
            Assert.All(run.Indices, i => Assert.InRange(i, 0, Chart.Count - 1));
        }

        [Fact]
        public void PatternUsesTheParameterValuesOfThePreview()
        {
            PatternSetup setup = PatternOf("hammer");
            int strict = Engine.RunPattern(setup, Chart, Engine.CreateEnvironment()).Indices.Count;

            setup.Config.Parameters.First(p => p.Id == "shadow").Value = 0;
            setup.Config.Parameters.First(p => p.Id == "max_upper").Value = 1;
            int loose = Engine.RunPattern(setup, Chart, Engine.CreateEnvironment()).Indices.Count;

            Assert.True(loose > strict, $"{loose} > {strict}");
        }

        [Fact]
        public void StrategyUsesPatternsFromTheRegistry()
        {
            var indicators = Examples.Indicators.Select(i => new IndicatorSetup(IndicatorDraft.Of(i), i.Formula)).ToList();
            var patterns = Examples.Patterns.Select(p => new PatternSetup(PatternDraft.Of(p), p.Formula)).ToList();
            var environment = Engine.CreateEnvironment(indicators, patterns);
            StrategyConfig strategy = Examples.Strategies.First(s => s.Id == "pattern-trend");

            StrategyRun run = Engine.RunStrategies([new StrategySetup(strategy.Id, strategy.Formula)], Chart, environment);

            StrategySignals signals = Assert.Single(run.Strategies);
            Assert.Null(signals.Error);
            Assert.All(signals.Signals, s => Assert.Contains(s, new double?[] { 0, 1 }));
        }

        [Fact]
        public void InvalidPatternIsNotInTheRegistry()
        {
            PatternSetup setup = PatternOf("bullish-engulfing");
            setup.Config.PatternId = "1BAD";
            var environment = Engine.CreateEnvironment(patterns: [setup]);

            StrategyRun run = Engine.RunStrategies([new StrategySetup("s", "IF(LOAD_PATTERN(\"ENGULFING\")[I], BUY, NEUTRAL)")], Chart, environment);
            Assert.Equal("INVALID_KEY_ERROR", run.Strategies[0].Error?.Name);
        }

        [Fact]
        public void TimeSessionExecutesFrames()
        {
            TimeRun run = Engine.RunTime("$p = IF(ISNULL(PREV), 0, PREV)\n$p + 10 * DTIME_S", Engine.CreateEnvironment());
            TimeSession session = run.Session;

            Assert.Null(session.PreparationError);
            Assert.Equal("0", session.Advance(0.3).Result.ToString());   // the first frame starts at 0
            Assert.Equal("5", session.Advance(0.5).Result.ToString());
            Assert.Equal("6", session.Advance(0.1).Result.ToString());
            Assert.Equal(0.6, session.TimeSeconds, 9);
            Assert.Equal(3, session.FrameCount);

            session.Reset();
            Assert.Empty(session.Frames);
            Assert.Equal("0", session.Advance(1).Result.ToString());
        }

        [Fact]
        public void TimeSessionKeepsTheLastFrames()
        {
            TimeSession session = Engine.RunTime("FRAME", Engine.CreateEnvironment()).Session;
            for (int i = 0; i < TimeSession.HistoryLimit + 5; i++)
                session.Advance(0.01);

            Assert.Equal(TimeSession.HistoryLimit, session.Frames.Count);
            Assert.Equal(TimeSession.HistoryLimit + 5, session.FrameCount);
            Assert.Equal(5, session.Frames[0].Frame);
        }

        [Fact]
        public void TimeSessionReportsAnInvalidFormula()
        {
            TimeSession session = Engine.RunTime("(1", Engine.CreateEnvironment()).Session;
            Assert.NotNull(session.PreparationError);
            Assert.True(session.Failed);
        }

        [Fact]
        public void EveryTimeExampleRuns()
        {
            foreach (Example example in Examples.ByContext[ContextKind.Time].Where(e => e.Id != Examples.NewFormula))
            {
                TimeSession session = Engine.RunTime(example.Formula.ReplaceLineEndings("\n"), Engine.CreateEnvironment()).Session;
                for (int i = 0; i < 5; i++)
                    Assert.True(session.Advance(0.05).Result.Successful, $"{example.Id}: {session.Last?.Result}");
            }
        }

        [Fact]
        public void EveryDefaultExampleExceptValidationRuns()
        {
            foreach (Example example in Examples.ByContext[ContextKind.Default].Where(e => e.Id is not (Examples.NewFormula or "validation")))
            {
                DefaultRun run = Engine.RunDefault(example.Formula.ReplaceLineEndings("\n"), Engine.CreateEnvironment());
                Assert.True(run.Execution.Successful, $"{example.Id}: {run.Execution}");
            }
        }

        [Fact]
        public void EveryStockExampleRuns()
        {
            foreach (Example example in Examples.ByContext[ContextKind.Stock].Where(e => e.Id != Examples.NewFormula))
            {
                IterationRun run = Engine.RunStock(example.Formula.ReplaceLineEndings("\n"), Chart, Engine.CreateEnvironment());
                Assert.True(run.Results.All(r => r.Successful), $"{example.Id}: {run.Results.FirstOrDefault(r => !r.Successful)}");
            }
        }

        [Fact]
        public void StrategyUsesIndicatorsFromTheRegistry()
        {
            var indicators = Examples.Indicators.Select(i => new IndicatorSetup(IndicatorDraft.Of(i), i.Formula)).ToList();
            var environment = Engine.CreateEnvironment(indicators);
            StrategyConfig crossover = Examples.Strategies.First(s => s.Id == "crossover");

            StrategyRun run = Engine.RunStrategies([new StrategySetup(crossover.Id, crossover.Formula)], Chart, environment);

            StrategySignals signals = Assert.Single(run.Strategies);
            Assert.Null(signals.Error);
            Assert.Equal(Chart.Count, signals.Signals.Count);
            Assert.All(signals.Signals, s => Assert.Contains(s, new double?[] { -1, 0, 1 }));
        }

        [Fact]
        public void SignalStaysActiveForItsLifetime()
        {
            double?[] signals = [0, 1, 0, 0, 0, -0.5, null];
            Assert.Equal([0, 1, 1, 1, 0, -0.5, -0.5], Engine.ActiveValues(signals, 3));
        }

        [Fact]
        public void NewSignalReplacesTheActiveOne()
            => Assert.Equal([1, -1, -1], Engine.ActiveValues([1, -1, 0], 5));

        [Fact]
        public void ProposalSumsOnlyActiveStrategies()
        {
            StrategySignals[] signals =
            [
                new("a", [1, 0, 0], null),
                new("b", [0, -1, 0], null),
                new("off", [1, 1, 1], null),
            ];
            var weights = new Dictionary<string, StrategyWeights>
            {
                ["a"] = new(true, 0.5, 2),
                ["b"] = new(true, 1, 1),
                ["off"] = new(false, 1, 1),
            };

            Proposal proposal = Engine.ProposalOf(signals, id => weights.GetValueOrDefault(id), 3);

            Assert.Equal([0.5, -0.5, 0], proposal.Final);
            Assert.Equal([0.5, 0, 0], proposal.Values["a"].Weighted.Select(v => v ?? double.NaN));
        }

        // Core 1.2 and Stock 1.2

        [Fact]
        public void DiagnoseGivesTheWarningsAndTheMetadata()
        {
            Example example = Examples.ByContext[ContextKind.Default].First(e => e.Id == "metadata");
            Diagnostics diagnostics = Engine.Diagnose(example.Formula.ReplaceLineEndings("\n"), Engine.CreateEnvironment());

            Assert.Equal(["UNUSED_VALUE", "UNUSED_VALUE", "CONTINUED_LINE"], diagnostics.Warnings.Select(w => w.Code));
            Assert.StartsWith("{ id: \"metadata-demo\"", diagnostics.Metadata);
        }

        [Fact]
        public void DiagnoseComparesTheContextOfTheMetadataWithTheContextType()
        {
            const string formula = "<@ { context: \"Stock.Strategy\" } @>\nNEUTRAL";

            Assert.Empty(Engine.Diagnose(formula, Engine.CreateEnvironment(), ContextKind.Strategy).Warnings);
            Assert.Equal(["METADATA_CONTEXT"], Engine.Diagnose(formula, Engine.CreateEnvironment(), ContextKind.Indicator).Warnings.Select(w => w.Code));
        }

        [Fact]
        public void EveryIterationExampleRuns()
        {
            foreach (Example example in Examples.ByContext[ContextKind.Iteration].Where(e => e.Id != Examples.NewFormula))
            {
                IterationRun run = Engine.RunIterations(example.Formula.ReplaceLineEndings("\n"), 12, Engine.CreateEnvironment());
                Assert.True(run.Results.All(r => r.Successful), $"{example.Id}: {run.Results.FirstOrDefault(r => !r.Successful)}");
            }
        }

        [Fact]
        public void MemoryExampleStartsTheCountAgainEveryFifthIteration()
        {
            Example example = Examples.ByContext[ContextKind.Iteration].First(e => e.Id == "memory");
            IterationRun run = Engine.RunIterations(example.Formula.ReplaceLineEndings("\n"), 7, Engine.CreateEnvironment());

            Assert.Equal("I = 5: sum = 5, stored before = 0", run.Results[5].ToString());
        }

        [Fact]
        public void StockContextHasTheTimeframeTheInstrumentAndTheWeeklyCandles()
        {
            IterationRun run = Engine.RunStock("[TIMEFRAME, INSTRUMENT, LEN(LOAD_CANDLES(\"1w\", { align: false }))]", Chart, Engine.CreateEnvironment());

            Assert.Equal("[\"1d\", \"MOCK\", 0]", run.Results[0].ToString());
            Assert.Equal($"[\"1d\", \"MOCK\", {Candles.Aggregate(Chart, 7 * 86_400_000.0).Count}]", run.Results[^1].ToString());
        }

        [Fact]
        public void EveryStrategyExampleRuns()
        {
            var indicators = Examples.Indicators.Select(i => new IndicatorSetup(IndicatorDraft.Of(i), i.Formula)).ToList();
            var patterns = Examples.Patterns.Select(p => new PatternSetup(PatternDraft.Of(p), p.Formula)).ToList();
            var environment = Engine.CreateEnvironment(indicators, patterns);

            var setups = Examples.Strategies.Where(s => s.Id != Examples.NewFormula).Select(s => new StrategySetup(s.Id, s.Formula.ReplaceLineEndings("\n"))).ToList();
            StrategyRun run = Engine.RunStrategies(setups, Chart, environment);

            Assert.All(run.Strategies, s => Assert.True(s.Error == null, $"{s.Id}: {s.Error}"));
        }

        [Fact]
        public void ScoreStrategyGivesSignalObjectsWithTheirChecks()
        {
            var indicators = Examples.Indicators.Select(i => new IndicatorSetup(IndicatorDraft.Of(i), i.Formula)).ToList();
            StrategyConfig score = Examples.Strategies.First(s => s.Id == "score");

            StrategySignals signals = Assert.Single(Engine.RunStrategies([new StrategySetup(score.Id, score.Formula.ReplaceLineEndings("\n"))],
                Chart, Engine.CreateEnvironment(indicators)).Strategies);

            Assert.Null(signals.Error);
            Assert.All(signals.Details, d => Assert.Equal(["trend", "rsi", "bullish"], d!.Checks.Select(c => c.Key)));
            Assert.All(signals.Signals.Zip(signals.Details).Where(p => p.First > 0), p => Assert.NotNull(p.Second!.Reason));
        }

        [Fact]
        public void RsiExampleKeepsItsAveragesInTheMemory()
        {
            IndicatorRun run = Engine.RunIndicator(SetupOf("rsi"), Chart, Engine.CreateEnvironment());

            Assert.Null(run.Error);
            IReadOnlyList<double?> rsi = run.Series["rsi"];
            Assert.All(rsi.Take(14), v => Assert.Null(v));
            Assert.All(rsi.Skip(14), v => Assert.InRange(v!.Value, 0, 100));
        }
    }
}
