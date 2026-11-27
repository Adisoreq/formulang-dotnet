using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core;
using FormuLang.NET.Core.Model;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Core.Structures;
using FormuLang.NET.Extensions.Stock.Contexts;

namespace FormuLang.NET.Extensions.Stock.Models
{
    /// <summary>
    /// The chart of a run (docs/implementation/formula_models.md, "Runs"): the candles of the Stock Context, with its
    /// options (<c>TIMEFRAME</c>, <c>INSTRUMENT</c>, other timeframes, the executor).
    /// </summary>
    public sealed class Chart
    {
        public IEnumerable<Candle> Candles { get; }

        /// <summary>Indicator only: the <c>SOURCE</c> (one Number or <c>null</c> for every candle); <c>CLOSE</c> by default.</summary>
        public IEnumerable<double?>? Source { get; init; }

        /// <inheritdoc cref="StockContextOptions.Executor"/>
        public Executor? Executor { get; init; }

        /// <inheritdoc cref="StockContextOptions.Timeframe"/>
        public string? Timeframe { get; init; }

        /// <inheritdoc cref="StockContextOptions.Instrument"/>
        public string? Instrument { get; init; }

        /// <inheritdoc cref="StockContextOptions.TimeframeLength"/>
        public double? TimeframeLength { get; init; }

        /// <inheritdoc cref="StockContextOptions.Timeframes"/>
        public IReadOnlyDictionary<string, TimeframeCandles>? Timeframes { get; init; }

        /// <inheritdoc cref="StockContextOptions.TimeframeProvider"/>
        public Func<string, TimeframeCandles?>? TimeframeProvider { get; init; }

        public Chart(IEnumerable<Candle> candles)
        {
            Candles = candles;
        }

        internal StockContextOptions Options(Executor executor) => new()
        {
            Executor = executor,
            Timeframe = Timeframe,
            Instrument = Instrument,
            TimeframeLength = TimeframeLength,
            Timeframes = Timeframes,
            TimeframeProvider = TimeframeProvider,
        };
    }

    /// <summary>
    /// Common properties of the result of a run, or of the part of a live run that new candles added.
    /// </summary>
    public abstract record RunResult
    {
        /// <summary>Index of the first candle of the result (<c>0</c> for a whole run).</summary>
        public int From { get; init; }

        /// <summary><c>SUCCESS</c>, or the first Error of the iterations of the result (or of its preparation).</summary>
        public IError Error { get; init; } = Errors.Success();

        public bool Successful => Error.IsSuccess();
    }

    /// <summary>
    /// One output of an indicator: one value (a Number or <c>null</c>) for every candle of the result.
    /// </summary>
    public sealed record IndicatorOutput(string Key, IReadOnlyList<double?> Values);

    /// <summary>
    /// The indicator series: for every output, one value for every candle of the result. The outputs are in the order
    /// of the outputs of the definition, then the other keys in the order of the results.
    /// </summary>
    public sealed record IndicatorSeries : RunResult
    {
        public IReadOnlyList<IndicatorOutput> Outputs { get; init; } = [];

        /// <summary>The keys of the outputs, in their order.</summary>
        public IEnumerable<string> Keys => Outputs.Select(o => o.Key);

        /// <summary>The values of an output.</summary>
        /// <exception cref="KeyNotFoundException">The indicator has no such output.</exception>
        public IReadOnlyList<double?> this[string key]
            => Outputs.FirstOrDefault(o => o.Key == key)?.Values ?? throw new KeyNotFoundException($"The indicator has no output \"{key}\".");

        public bool TryGetOutput(string key, out IReadOnlyList<double?> values)
        {
            values = Outputs.FirstOrDefault(o => o.Key == key)?.Values ?? [];
            return Outputs.Any(o => o.Key == key);
        }
    }

    /// <summary>
    /// The pattern series: <c>true</c> for every candle of the result where the pattern is found.
    /// </summary>
    public sealed record PatternSeries : RunResult
    {
        public IReadOnlyList<bool> Matches { get; init; } = [];
    }

    /// <summary>
    /// A signal of a strategy (docs/implementation/formula_models.md, "Runs").
    /// </summary>
    /// <param name="Index">Index of the candle, from <c>0</c>.</param>
    /// <param name="Time"><c>time</c> of the candle (Unix time in milliseconds), or <c>null</c>.</param>
    /// <param name="Signal">The signal, from <c>-1</c> (<c>SELL</c>) to <c>1</c> (<c>BUY</c>), clamped.</param>
    /// <param name="Reason"><c>reason</c> of the signal object, or <c>null</c>.</param>
    /// <param name="Checks"><c>checks</c> of the signal object, or <c>null</c>.</param>
    /// <param name="Extra">The other keys of the signal object (e.g. <c>stop</c>, <c>target</c>), as .NET values (<see cref="ValueConversion.ToClr"/>).</param>
    public sealed record StrategySignal(int Index, double? Time, double Signal, string? Reason,
        IReadOnlyDictionary<string, bool>? Checks, IReadOnlyDictionary<string, object?> Extra)
    {
        public bool IsBuy => Signal > 0;
        public bool IsSell => Signal < 0;

        /// <summary>A Number value of <see cref="Extra"/>, or <c>null</c>.</summary>
        public double? Number(string key) => Extra.TryGetValue(key, out object? value) && value is double d ? d : null;
    }

    /// <summary>
    /// The signals of a strategy: one for every candle of the result whose signal is not <c>NEUTRAL</c>.
    /// </summary>
    public sealed record StrategySignals : RunResult
    {
        public IReadOnlyList<StrategySignal> Signals { get; init; } = [];

        /// <summary>
        /// The result of every candle of the result (a Number, a signal object or an Error), e.g. to explain the checks.
        /// </summary>
        public IReadOnlyList<IValue> Results { get; init; } = [];
    }

    /// <summary>
    /// A run of an instance that can be continued (docs/implementation/formula_models.md, "Live runs"):
    /// <see cref="Start"/> executes the formula for the candles of the chart, <see cref="Append"/> adds the candles that
    /// closed later and executes only them, with the same results as a new run over all the candles. The state can be
    /// saved and restored.
    /// </summary>
    public abstract class LiveRun<TResult> where TResult : RunResult
    {
        private readonly Executor _executor;
        private readonly string _formula;
        private PreparedFormula? _prepared;
        private readonly List<IValue> _values = [];

        /// <summary>
        /// The Stock context of the run.
        /// </summary>
        public StockContext Context { get; }

        private protected LiveRun(string formula, StockContext context, Executor executor)
        {
            _formula = formula;
            Context = context;
            _executor = executor;
        }

        /// <summary>
        /// The results of every executed candle.
        /// </summary>
        public IReadOnlyList<IValue> Values => _values;

        /// <summary>
        /// Executes the formula for every candle of the chart, from the first one.
        /// </summary>
        public TResult Start()
        {
            PreparedFormula prepared = Prepare();

            if (prepared.Failure != null)
                return Failed(0, prepared.Failure.Error);

            _values.Clear();
            return Collect(_executor.ExecuteIterations(prepared, Context, stopOnError: false), 0);
        }

        /// <summary>
        /// Adds the candles that closed after the last one, and executes the formula only for them.
        /// </summary>
        /// <returns>The result of the new candles only (<see cref="RunResult.From"/> is the index of the first one).</returns>
        public TResult Append(IEnumerable<Candle> candles)
        {
            int from = _values.Count;
            AddCandles(candles);
            PreparedFormula prepared = Prepare();

            if (prepared.Failure != null)
                return Failed(from, prepared.Failure.Error);

            if (from == 0)
                return Start();

            return Collect(_executor.ResumeIterations(prepared, Context, stopOnError: false), from);
        }

        /// <summary>
        /// The state of the executed candles (<c>PREV</c>, <c>HISTORY</c>, the memory), e.g. to store it in the host
        /// application.
        /// </summary>
        public IterationState SaveState() => Context.SaveState();

        /// <summary>
        /// Restores a saved state in a live run over the same candles; the next <see cref="Append"/> continues after it.
        /// </summary>
        /// <exception cref="ArgumentException">The state does not fit the candles.</exception>
        public void RestoreState(IterationState state)
        {
            Context.RestoreState(state);
            _values.Clear();

            if (state.CompletedIterations > 0 && state.LastResult != null)
            {
                _values.AddRange(state.History);
                _values.Add(state.LastResult);
            }
        }

        private PreparedFormula Prepare() => _prepared ??= _executor.Prepare(_formula, Context);

        private TResult Collect(IterationExecutionResult run, int from)
        {
            _values.AddRange(run.Values);
            return Convert(_values, from);
        }

        /// <summary>
        /// Adds the candles to the context.
        /// </summary>
        private protected virtual void AddCandles(IEnumerable<Candle> candles) => Context.AppendCandles(candles);

        /// <summary>
        /// The result of the candles from <paramref name="from"/>, of all the results.
        /// </summary>
        private protected abstract TResult Convert(IReadOnlyList<IValue> values, int from);

        /// <summary>
        /// The result of a formula that cannot be prepared.
        /// </summary>
        private protected abstract TResult Failed(int from, IError error);

        /// <summary>
        /// The first Error among the values from <paramref name="from"/>.
        /// </summary>
        private protected static IError? FirstError(IReadOnlyList<IValue> values, int from)
        {
            for (int i = from; i < values.Count; i++)
            {
                if (values[i].Type.Base == Types.ErrorName && values[i].Raw is IError error && error.IsFailed())
                    return error;
            }

            return null;
        }
    }

    /// <summary>
    /// A live run of an indicator instance.
    /// </summary>
    public sealed class LiveIndicatorRun : LiveRun<IndicatorSeries>
    {
        private readonly IReadOnlyList<string>? _outputs;
        private IEnumerable<double?>? _appendedSource;

        internal LiveIndicatorRun(string formula, IObject parameters, IReadOnlyList<string>? outputs, IEnvironment environment, Chart chart)
            : this(formula, parameters, outputs, environment, chart, chart.Executor ?? Executor.Create()) { }

        private LiveIndicatorRun(string formula, IObject parameters, IReadOnlyList<string>? outputs, IEnvironment environment, Chart chart, Executor executor)
            : base(formula, IndicatorContext.Create(environment, chart.Candles, chart.Options(executor), parameters, chart.Source), executor)
        {
            _outputs = outputs;
        }

        /// <summary>
        /// Adds the candles that closed after the last one, and executes the indicator only for them.
        /// </summary>
        /// <param name="source">With a <c>SOURCE</c> of the chart: its values of the new candles (one for every candle).</param>
        public IndicatorSeries Append(IEnumerable<Candle> candles, IEnumerable<double?>? source)
        {
            _appendedSource = source;

            try
            {
                return Append(candles);
            }
            finally
            {
                _appendedSource = null;
            }
        }

        private protected override void AddCandles(IEnumerable<Candle> candles)
            => ((IndicatorContext)Context).AppendCandles(candles, _appendedSource);

        private protected override IndicatorSeries Convert(IReadOnlyList<IValue> values, int from)
        {
            if (FirstError(values, from) is IError error)
                return Failed(from, error);

            if (StockExecutor.IndicatorResultsError(values) is IValue problem)
                return Failed(from, (IError)problem.Raw!);

            var outputs = new List<IndicatorOutput>();

            foreach (string key in OutputKeys(values, _outputs))
            {
                var series = new double?[values.Count - from];

                for (int i = from; i < values.Count; i++)
                {
                    IValue value = values[i];

                    series[i - from] = value.Type.Base switch
                    {
                        Types.NumberName => key == "value" ? (double)value.Raw! : null,
                        Types.ObjectName when value.Raw is IObject obj && obj.HasKey(key) => obj.GetProperty(key).GetValue().Raw as double?,
                        _ => null,
                    };
                }

                outputs.Add(new IndicatorOutput(key, series));
            }

            return new IndicatorSeries { From = from, Outputs = outputs };
        }

        private protected override IndicatorSeries Failed(int from, IError error) => new() { From = from, Error = error };

        /// <summary>
        /// The outputs of an indicator: the outputs of the definition, then the other keys of the results in their order
        /// (<c>value</c> for Number results); without results other than <c>null</c>, the outputs of the definition or
        /// <c>value</c>.
        /// </summary>
        private static List<string> OutputKeys(IReadOnlyList<IValue> values, IReadOnlyList<string>? declared)
        {
            var keys = new List<string>(declared ?? []);

            foreach (IValue value in values)
            {
                if (value.Type.Base == Types.NumberName && !keys.Contains("value"))
                    keys.Add("value");

                if (value.Type.Base == Types.ObjectName && value.Raw is IObject obj)
                {
                    foreach (string key in obj.Keys)
                    {
                        if (!keys.Contains(key))
                            keys.Add(key);
                    }
                }
            }

            return keys.Count > 0 ? keys : ["value"];
        }
    }

    /// <summary>
    /// A live run of a pattern instance.
    /// </summary>
    public sealed class LivePatternRun : LiveRun<PatternSeries>
    {
        internal LivePatternRun(string formula, IObject parameters, IEnvironment environment, Chart chart)
            : this(formula, parameters, environment, chart, chart.Executor ?? Executor.Create()) { }

        private LivePatternRun(string formula, IObject parameters, IEnvironment environment, Chart chart, Executor executor)
            : base(formula, PatternContext.Create(environment, chart.Candles, chart.Options(executor), parameters), executor) { }

        private protected override PatternSeries Convert(IReadOnlyList<IValue> values, int from)
        {
            if (FirstError(values, from) is IError error)
                return Failed(from, error);

            var matches = new List<bool>(values.Count - from);

            for (int i = from; i < values.Count; i++)
            {
                if (values[i].Type.Base != Types.BooleanName)
                    return Failed(from, StockExecutor.PatternResultError(i));

                matches.Add(values[i].Raw is true);
            }

            return new PatternSeries { From = from, Matches = matches };
        }

        private protected override PatternSeries Failed(int from, IError error) => new() { From = from, Error = error };
    }

    /// <summary>
    /// A live run of a strategy instance.
    /// </summary>
    public sealed class LiveStrategyRun : LiveRun<StrategySignals>
    {
        internal LiveStrategyRun(string formula, IObject parameters, IEnvironment environment, Chart chart)
            : this(formula, parameters, environment, chart, chart.Executor ?? Executor.Create()) { }

        private LiveStrategyRun(string formula, IObject parameters, IEnvironment environment, Chart chart, Executor executor)
            : base(formula, StrategyContext.Create(environment, chart.Candles, chart.Options(executor), parameters), executor) { }

        private protected override StrategySignals Convert(IReadOnlyList<IValue> values, int from)
        {
            IValue[] results = values.Skip(from).ToArray();

            if (FirstError(values, from) is IError error)
                return new StrategySignals { From = from, Results = results, Error = error };

            var signals = new List<StrategySignal>();

            for (int i = from; i < values.Count; i++)
            {
                if (!TrySignal(values[i], i, Context.Candles[i], out StrategySignal? signal))
                    return new StrategySignals { From = from, Results = results, Error = StockExecutor.StrategyResultError(i) };

                if (signal != null)
                    signals.Add(signal);
            }

            return new StrategySignals { From = from, Signals = signals, Results = results };
        }

        private protected override StrategySignals Failed(int from, IError error) => new() { From = from, Error = error };

        private static double Clamp(double signal) => Math.Clamp(signal, StrategyContext.Sell, StrategyContext.Buy);

        /// <summary>
        /// The signal of a result: <c>null</c> for <c>NEUTRAL</c>; <c>false</c> for a value that is neither a Number nor a
        /// signal object (the signal object itself is checked in the finalization of the iteration).
        /// </summary>
        private static bool TrySignal(IValue value, int index, Candle candle, out StrategySignal? signal)
        {
            signal = null;
            double? time = candle.Time;

            if (value.Type.Base == Types.NumberName)
            {
                double number = Clamp((double)value.Raw!);

                if (number != 0)
                    signal = new StrategySignal(index, time, number, null, null, new Dictionary<string, object?>());

                return true;
            }

            if (value.Type.Base != Types.ObjectName || value.Raw is not IObject obj)
                return false;

            double clamped = Clamp((double)obj.GetProperty("signal").GetValue().Raw!);

            if (clamped == 0)
                return true;

            string? reason = null;
            Dictionary<string, bool>? checks = null;
            var extra = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (string key in obj.Keys)
            {
                IValue property = obj.GetProperty(key).GetValue();

                switch (key)
                {
                    case "signal":
                        break;
                    case "reason":
                        reason = property.Raw as string;
                        break;
                    case "checks":
                        var checksObject = (IObject)property.Raw!;
                        checks = checksObject.Keys.ToDictionary(k => k, k => checksObject.GetProperty(k).GetValue().Raw is true, StringComparer.Ordinal);
                        break;
                    default:
                        extra[key] = ValueConversion.ToClr(property);
                        break;
                }
            }

            signal = new StrategySignal(index, time, clamped, reason, checks, extra);
            return true;
        }
    }
}
