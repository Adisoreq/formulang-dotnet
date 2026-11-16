using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Core.Structures;
using FormuLang.NET.Extensions.Stock;
using FormuLang.NET.Extensions.Stock.Contexts;
using FormuLang.NET.Extensions.Stock.Indicators;
using FlEnvironment = FormuLang.NET.Core.Structures.Environment;
using FlObject = FormuLang.NET.Core.Primitives.Object;

namespace WebPreview.Model
{
    /// <summary>An indicator of the preview with its current formula.</summary>
    public sealed record IndicatorSetup(IndicatorDraft Config, string Formula);

    /// <summary>Problems found before the evaluation: every validation error, and the canonical form of every sequence.</summary>
    /// <param name="Sequences">Canonical form of every sequence, e.g. <c>SET($x,ADD(1,2))</c>; empty if the formula does not parse.</param>
    public sealed record Diagnostics(IReadOnlyList<IError> ValidationErrors, IReadOnlyList<string> Sequences);

    /// <param name="Changed">An environment variable changed by the formula.</param>
    public sealed record VariableRow(string Name, string Value, bool Changed);

    /// <summary>The result of the execution of a context type.</summary>
    public abstract record Run;

    public sealed record DefaultRun(ExecutionResult Execution, IReadOnlyList<VariableRow> Variables) : Run;

    /// <param name="Results">Results of the executed iterations; fewer than the count if an iteration failed.</param>
    public sealed record IterationRun(IReadOnlyList<ExecutionResult> Results, int Count) : Run;

    /// <param name="Series">Values of every series by its id; <c>null</c> where the indicator has no value.</param>
    public sealed record IndicatorRun(IReadOnlyDictionary<string, IReadOnlyList<double?>> Series, IError? Error) : Run;

    /// <param name="Indices">Indices of the candles where the pattern ends.</param>
    public sealed record PatternRun(IReadOnlyList<int> Indices, IError? Error) : Run;

    /// <param name="Id">Key of the strategy in the preview.</param>
    public sealed record StrategySetup(string Id, string Formula);

    /// <param name="Signals">Result of every candle, from -1 to 1 (clamped); <c>null</c> where the formula failed.</param>
    public sealed record StrategySignals(string Id, IReadOnlyList<double?> Signals, IError? Error);

    /// <summary>Signals of every strategy. Confidence, lifetime and the proposal are applied by <see cref="Engine.ProposalOf"/>.</summary>
    public sealed record StrategyRun(IReadOnlyList<StrategySignals> Strategies) : Run;

    /// <summary>Settings of a strategy that the proposal uses.</summary>
    public sealed record StrategyWeights(bool Active, double Confidence, double Lifetime);

    /// <param name="Weighted">Signal × confidence of every candle; <c>null</c> where the formula failed.</param>
    /// <param name="Active">Value of the strategy for every candle: the last weighted signal while it is active (its lifetime).</param>
    public sealed record StrategyValues(IReadOnlyList<double?> Weighted, IReadOnlyList<double> Active);

    public sealed record Proposal(IReadOnlyDictionary<string, StrategyValues> Values, IReadOnlyList<double> Final);

    /// <summary>Execution of the formulas of every context type with FormuLang.NET.</summary>
    public static class Engine
    {
        private static readonly Executor Executor = Executor.Create();

        /// <summary>
        /// An environment with the Core and Stock standards. Every indicator of the preview with a valid definition is in
        /// the registry, with its current formula and the default values of its parameters, so <c>INDICATOR("SMA")</c>
        /// works in every Stock formula.
        /// </summary>
        public static FlEnvironment CreateEnvironment(IReadOnlyList<IndicatorSetup>? indicators = null)
        {
            var environment = FlEnvironment.Create();
            IndicatorRegistry registry = environment.RegisterStock().Registry;
            indicators ??= [];
            var drafts = indicators.Select(i => i.Config).ToList();

            foreach (var (config, formula) in indicators)
            {
                if (config.Problems(drafts).Count > 0)
                    continue;

                registry.Add(config.IndicatorId, formula,
                    config.Parameters.Select(p => new IndicatorParameter(p.Id, p.DefaultValue)).ToArray());
            }

            return environment;
        }

        /// <summary>The environment used for the suggestions and the highlighting of the editor.</summary>
        public static readonly FlEnvironment ReferenceEnvironment = CreateEnvironment();

        private static IEnumerable<Candle> ToCandles(IReadOnlyList<ChartCandle> candles)
            => candles.Select(c => new Candle(c.Open, c.High, c.Low, c.Close, c.Volume, c.Time));

        public static Diagnostics Diagnose(string formula, FlEnvironment environment)
        {
            IReadOnlyList<IError> validationErrors = Validator.Validate(formula, environment).Errors;

            CleanUpResult cleaned = Cleaner.CleanUp(formula);
            if (cleaned.Error.IsFailed())
                return new(validationErrors, []);

            TokenizationResult tokens = Tokenizer.Tokenize(cleaned.Result);
            if (tokens.Error.IsFailed())
                return new(validationErrors, []);

            ParsingResult parsed = Parser.ForEnvironment(environment).Parse(tokens.Tokens);
            return new(validationErrors, parsed.Error.IsSuccess() ? parsed.Sequences.Select(s => s.Root.Key).ToList() : []);
        }

        public static DefaultRun RunDefault(string formula, FlEnvironment environment)
        {
            var context = Context.Create(environment);
            ExecutionResult execution = Executor.Execute(formula, context);
            var variables = new List<VariableRow>();

            foreach (string key in context.Keys)
            {
                IContainer container = context.GetProperty(key);
                if (container.ContainerType == ContainerType.Variable)
                    variables.Add(new($"${key}", container.GetValue().ToString() ?? "", false));
            }

            foreach (string key in context.EnvironmentVariables.Keys)
            {
                string value = context.EnvironmentVariables.GetProperty(key).GetValue().ToString() ?? "";
                string? initial = environment.HasKey(key) ? environment.GetProperty(key).GetValue().ToString() : null;
                variables.Add(new($"@{key}", value, value != initial));
            }

            return new DefaultRun(execution, variables);
        }

        public static IterationRun RunIterations(string formula, int count, FlEnvironment environment)
        {
            IterationExecutionResult run = Executor.ExecuteIterations(formula, IterationContext.Create(environment, count));
            return new IterationRun(run.Results, count);
        }

        public static IterationRun RunStock(string formula, IReadOnlyList<ChartCandle> candles, FlEnvironment environment)
        {
            var context = StockContext.Create(environment, ToCandles(candles), Executor);
            IterationExecutionResult run = Executor.ExecuteIterations(formula, context);
            return new IterationRun(run.Results, candles.Count);
        }

        /// <summary>Runs an indicator formula and splits its results into the configured series.</summary>
        public static IndicatorRun RunIndicator(IndicatorSetup setup, IReadOnlyList<ChartCandle> candles, FlEnvironment environment)
        {
            IndicatorDraft config = setup.Config;
            var context = IndicatorContext.Create(environment, ToCandles(candles), ParametersOf(setup), executor: Executor);
            IValue result = Executor.ExecuteIndicator(setup.Formula, context);

            var series = new Dictionary<string, List<double?>>(StringComparer.Ordinal);
            foreach (SeriesDraft s in config.Series)
                series.TryAdd(s.Id, []);

            IReadOnlyDictionary<string, IReadOnlyList<double?>> Done()
                => series.ToDictionary(p => p.Key, p => (IReadOnlyList<double?>)p.Value, StringComparer.Ordinal);

            if (result.Type.Base == Types.ErrorName)
                return new IndicatorRun(Done(), (IError)result.Raw!);

            foreach (IValue value in (IValue[])result.Raw!)
            {
                foreach (SeriesDraft s in config.Series.DistinctBy(s => s.Id))
                {
                    object? number = null;
                    if (config.SeriesMode == SeriesMode.Single)
                        number = value.Raw;
                    else if (value.Type.Base == Types.ObjectName && value.Raw is IObject obj && obj.HasKey(s.Id))
                        number = obj.GetProperty(s.Id).GetValue().Raw;

                    series[s.Id].Add(number is double d ? d : null);
                }
            }

            return new IndicatorRun(Done(), null);
        }

        /// <summary><c>PARAMETERS</c> of the Indicator Context: the values of the parameters set in the preview.</summary>
        private static FlObject ParametersOf(IndicatorSetup setup)
        {
            var parameters = new FlObject();
            foreach (IndicatorParameterDraft p in setup.Config.Parameters)
                parameters.InitVariable(p.Id, Value.Number(p.Value, Names.Number(p.Value)));
            return parameters;
        }

        public static PatternRun RunPattern(string formula, IReadOnlyList<ChartCandle> candles, FlEnvironment environment)
        {
            var context = PatternContext.Create(environment, ToCandles(candles), Executor);
            PatternResult result = Executor.ExecutePattern(formula, context);
            return new PatternRun(result.Indices, result.Successful ? null : result.Error);
        }

        public static StrategyRun RunStrategies(IReadOnlyList<StrategySetup> setups, IReadOnlyList<ChartCandle> candles, FlEnvironment environment)
        {
            var strategies = setups.Select(setup =>
            {
                var context = StrategyContext.Create(environment, ToCandles(candles), Executor);
                StrategyResult result = Executor.ExecuteStrategy(setup.Formula, context, stopOnError: false);
                var signals = Enumerable.Range(0, candles.Count).Select(i => i < result.Signals.Count ? result.Signals[i] : null).ToList();
                return new StrategySignals(setup.Id, signals, result.Successful ? null : result.Error);
            }).ToList();

            return new StrategyRun(strategies);
        }

        /// <summary>Values of a strategy: a signal stays active for <paramref name="lifetime"/> candles, or until the strategy gives a new one.</summary>
        public static List<double> ActiveValues(IReadOnlyList<double?> signals, double lifetime)
        {
            var values = new List<double>(signals.Count);
            double value = 0;
            double remaining = 0;

            foreach (double? signal in signals)
            {
                if (signal is double s && s != 0)
                {
                    value = s;
                    remaining = Math.Max(1, Math.Floor(lifetime));
                }

                values.Add(remaining > 0 ? value : 0);
                remaining--;
            }

            return values;
        }

        /// <summary>
        /// Values of every strategy and the final proposal of every candle: the sum of the values of all active
        /// strategies. Computed from the signals, so a change of a weight needs no new execution.
        /// </summary>
        public static Proposal ProposalOf(IReadOnlyList<StrategySignals> signals, Func<string, StrategyWeights?> weights, int count)
        {
            var values = new Dictionary<string, StrategyValues>(StringComparer.Ordinal);
            foreach (StrategySignals strategy in signals)
            {
                StrategyWeights? w = weights(strategy.Id);
                if (w == null)
                    continue;

                var weighted = strategy.Signals.Select(signal => signal * w.Confidence).ToList();
                values[strategy.Id] = new StrategyValues(weighted, ActiveValues(weighted, w.Lifetime));
            }

            var proposal = Enumerable.Range(0, count).Select(i => signals.Sum(s =>
                weights(s.Id)?.Active == true && values.TryGetValue(s.Id, out StrategyValues? v) && i < v.Active.Count ? v.Active[i] : 0))
                .ToList();

            return new Proposal(values, proposal);
        }
    }
}
