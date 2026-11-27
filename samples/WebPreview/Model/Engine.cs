using System.Runtime.CompilerServices;
using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Processing;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Model;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Core.Structures;
using FormuLang.NET.Core.Utils;
using FormuLang.NET.Extensions.Stock;
using FormuLang.NET.Extensions.Stock.Contexts;
using FormuLang.NET.Extensions.Stock.Registry;
using FlEnvironment = FormuLang.NET.Core.Structures.Environment;
using FlObject = FormuLang.NET.Core.Primitives.Object;

namespace WebPreview.Model
{
    /// <summary>An indicator of the preview with its current formula.</summary>
    public sealed record IndicatorSetup(IndicatorDraft Config, string Formula);

    /// <summary>A pattern of the preview with its current formula.</summary>
    public sealed record PatternSetup(PatternDraft Config, string Formula);

    /// <summary>
    /// Result of the preparation (Core 1.2): every validation error (or the error of the metadata reading, clean-up,
    /// tokenization or parsing), the parsed sequences, the warnings and the metadata of the formula.
    /// </summary>
    /// <param name="Sequences">Canonical form of every sequence, e.g. <c>SET($x,ADD(1,2))</c>; empty if the formula does not parse.</param>
    /// <param name="Warnings">Warnings of the preparation, in the order of their positions.</param>
    /// <param name="Metadata">The metadata block of the formula, serialized; <c>null</c> without one.</param>
    public sealed record Diagnostics(IReadOnlyList<IError> ValidationErrors, IReadOnlyList<string> Sequences,
        IReadOnlyList<IWarning> Warnings, string? Metadata)
    {
        public Diagnostics(IReadOnlyList<IError> validationErrors, IReadOnlyList<string> sequences)
            : this(validationErrors, sequences, [], null) { }
    }

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

    /// <summary>The frames of a Time Context, executed by the output of the preview while it plays.</summary>
    public sealed record TimeRun(TimeSession Session) : Run;

    /// <param name="Id">Key of the strategy in the preview.</param>
    public sealed record StrategySetup(string Id, string Formula);

    /// <summary><c>reason</c> and <c>checks</c> of a signal object (Stock 1.2).</summary>
    /// <param name="Extra">The other keys of the signal object, e.g. <c>stop</c>, serialized.</param>
    public sealed record SignalDetails(string? Reason, IReadOnlyList<KeyValuePair<string, bool>> Checks, IReadOnlyList<KeyValuePair<string, string>> Extra);

    /// <param name="Signals">Result of every candle, from -1 to 1 (clamped); <c>null</c> where the formula failed.</param>
    /// <param name="Details">The signal object of every candle; <c>null</c> where the formula returned a Number.</param>
    public sealed record StrategySignals(string Id, IReadOnlyList<double?> Signals, IError? Error)
    {
        public IReadOnlyList<SignalDetails?> Details { get; init; } = [];
    }

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
        /// An environment with the Core and Stock standards. Every indicator and pattern of the preview with a valid
        /// definition is in the formula registry, with its current formula and the default values of its parameters,
        /// so <c>LOAD_INDICATOR("SMA")</c> and <c>LOAD_PATTERN("ENGULFING")</c> work in every Stock formula.
        /// </summary>
        public static FlEnvironment CreateEnvironment(IReadOnlyList<IndicatorSetup>? indicators = null, IReadOnlyList<PatternSetup>? patterns = null)
        {
            var environment = FlEnvironment.Create();
            FormulaRegistry registry = environment.RegisterStock().Registry;

            indicators ??= [];
            var indicatorDrafts = indicators.Select(i => i.Config).ToList();
            foreach (var (config, formula) in indicators.Where(i => i.Config.Problems(indicatorDrafts).Count == 0))
                registry.Indicators.Add(config.IndicatorId, formula, DefinitionParameters(config.Parameters));

            patterns ??= [];
            var patternDrafts = patterns.Select(p => p.Config).ToList();
            foreach (var (config, formula) in patterns.Where(p => p.Config.Problems(patternDrafts).Count == 0))
                registry.Patterns.Add(config.PatternId, formula, DefinitionParameters(config.Parameters));

            return environment;
        }

        private static FormulaParameter[] DefinitionParameters(IEnumerable<IndicatorParameterDraft> parameters)
            => parameters.Select(p => new FormulaParameter(p.Id, p.DefaultValue)).ToArray();

        /// <summary>The environment used for the suggestions and the highlighting of the editor.</summary>
        public static readonly FlEnvironment ReferenceEnvironment = CreateEnvironment();

        private static IEnumerable<Candle> ToCandles(IReadOnlyList<ChartCandle> candles)
            => candles.Select(c => new Candle(c.Open, c.High, c.Low, c.Close, c.Volume, c.Time));

        /// <summary>Timeframe of the mock candles (daily) and the other timeframe the preview provides (weekly).</summary>
        public const string Timeframe = "1d";
        public const string OtherTimeframe = "1w";
        public const string Instrument = "MOCK";

        private const double Day = 24 * 60 * 60 * 1000;

        private static readonly ConditionalWeakTable<IReadOnlyList<ChartCandle>, Candle[]> Weekly = new();

        /// <summary>
        /// Options of every Stock context of the preview (Stock 1.2): <c>TIMEFRAME</c>, <c>INSTRUMENT</c>, and the weekly
        /// candles built from the daily ones, for <c>LOAD_CANDLES("1w")</c> and the <c>timeframe</c> option of
        /// <c>LOAD_INDICATOR</c> / <c>LOAD_PATTERN</c>.
        /// </summary>
        private static StockContextOptions ChartOptions(IReadOnlyList<ChartCandle> candles)
        {
            Candle[] week = Weekly.GetValue(candles, c => ToCandles(Candles.Aggregate(c, 7 * Day)).ToArray());

            return new StockContextOptions
            {
                Executor = Executor,
                Timeframe = Timeframe,
                Instrument = Instrument,
                TimeframeLength = Day,
                Timeframes = new Dictionary<string, TimeframeCandles> { [OtherTimeframe] = new(7 * Day, week) },
            };
        }

        /// <summary>A context of the kind, for the preparation (the <c>METADATA_CONTEXT</c> warning compares it with the metadata).</summary>
        private static IContext ContextOf(ContextKind kind, FlEnvironment environment) => kind switch
        {
            ContextKind.Iteration => IterationContext.Create(environment, 1),
            ContextKind.Time => TimeContext.Create(environment),
            ContextKind.Stock => StockContext.Create(environment, [], Executor),
            ContextKind.Indicator => IndicatorContext.Create(environment, [], executor: Executor),
            ContextKind.Pattern => PatternContext.Create(environment, [], Executor),
            ContextKind.Strategy => StrategyContext.Create(environment, [], Executor),
            _ => Context.Create(environment),
        };

        public static Diagnostics Diagnose(string formula, FlEnvironment environment, ContextKind kind = ContextKind.Default)
        {
            PreparedFormula prepared = Executor.Prepare(formula, ContextOf(kind, environment));

            if (prepared.Failure is ExecutionResult failure)
                return new(failure.ValidationErrors, [], [], null);

            string? metadata = prepared.Metadata.Raw is IObject { } obj && !obj.IsEmpty() ? StringConverter.ValueToString(prepared.Metadata) : null;
            return new([], prepared.Sequences.Select(s => ((Sequence)s).Root.Key).ToList(), prepared.Warnings, metadata);
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
            var context = StockContext.Create(environment, ToCandles(candles), ChartOptions(candles));
            IterationExecutionResult run = Executor.ExecuteIterations(formula, context);
            return new IterationRun(run.Results, candles.Count);
        }

        /// <summary>Runs an indicator formula and splits its results into the configured series.</summary>
        public static IndicatorRun RunIndicator(IndicatorSetup setup, IReadOnlyList<ChartCandle> candles, FlEnvironment environment)
        {
            IndicatorDraft config = setup.Config;
            var context = IndicatorContext.Create(environment, ToCandles(candles), ChartOptions(candles), ParametersOf(setup));
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
        private static FlObject ParametersOf(IndicatorSetup setup) => ParametersOf(setup.Config.Parameters);

        private static FlObject ParametersOf(IEnumerable<IndicatorParameterDraft> drafts)
        {
            var parameters = new FlObject();
            foreach (IndicatorParameterDraft p in drafts.DistinctBy(p => p.Id))
                parameters.InitVariable(p.Id, Value.Number(p.Value, Names.Number(p.Value)));
            return parameters;
        }

        /// <summary>Runs a pattern formula with <c>PARAMETERS</c> = the values of the parameters set in the preview.</summary>
        public static PatternRun RunPattern(PatternSetup setup, IReadOnlyList<ChartCandle> candles, FlEnvironment environment)
        {
            var context = PatternContext.Create(environment, ToCandles(candles), ChartOptions(candles), ParametersOf(setup.Config.Parameters));
            PatternResult result = Executor.ExecutePattern(setup.Formula, context);
            return new PatternRun(result.Indices, result.Successful ? null : result.Error);
        }

        /// <summary>Prepares the frames of a Time Context; the output of the preview executes them as time passes.</summary>
        public static TimeRun RunTime(string formula, FlEnvironment environment)
            => new(new TimeSession(Executor.CreateFrameRunner(formula, TimeContext.Create(environment))));

        public static StrategyRun RunStrategies(IReadOnlyList<StrategySetup> setups, IReadOnlyList<ChartCandle> candles, FlEnvironment environment)
        {
            var strategies = setups.Select(setup =>
            {
                // PARAMETERS is completed with the defaults of the metadata of the strategy
                var context = StrategyContext.Create(environment, ToCandles(candles), ChartOptions(candles));
                StrategyResult result = Executor.ExecuteStrategy(setup.Formula, context, stopOnError: false);
                var signals = Enumerable.Range(0, candles.Count).Select(i => i < result.Signals.Count ? result.Signals[i] : null).ToList();
                var details = Enumerable.Range(0, candles.Count)
                    .Select(i => i < result.SignalObjects.Count ? DetailsOf(result.SignalObjects[i]) : null).ToList();
                return new StrategySignals(setup.Id, signals, result.Successful ? null : result.Error) { Details = details };
            }).ToList();

            return new StrategyRun(strategies);
        }

        private static SignalDetails? DetailsOf(IObject? signal)
        {
            if (signal == null)
                return null;

            string? reason = null;
            var checks = new List<KeyValuePair<string, bool>>();
            var extra = new List<KeyValuePair<string, string>>();

            foreach (string key in signal.Keys)
            {
                IValue value = signal.GetProperty(key).GetValue();

                switch (key)
                {
                    case "signal":
                        break;
                    case "reason":
                        reason = value.Raw as string;
                        break;
                    case "checks" when value.Raw is IObject obj:
                        checks.AddRange(obj.Keys.Select(k => new KeyValuePair<string, bool>(k, obj.GetProperty(k).GetValue().Raw is true)));
                        break;
                    default:
                        extra.Add(new(key, System.Text.Json.JsonSerializer.Serialize(ValueConversion.ToClr(value))));
                        break;
                }
            }

            return new SignalDetails(reason, checks, extra);
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
