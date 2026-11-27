using FormuLang.NET.Abstractions.Functions;
using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Core.Structures;

namespace FormuLang.NET.Extensions.Stock.Contexts
{
    /// <summary>
    /// <b>Stock Context</b> (docs/standards/stock/README.md, "Stock Context"), derived from the Core Iteration Context:
    /// the formula is executed once for every candle, <c>I</c> is the index of the current candle and <c>N</c> is the
    /// number of candles. Constants: <c>CANDLES</c>, <c>OPEN</c>, <c>HIGH</c>, <c>LOW</c>, <c>CLOSE</c>, <c>VOLUME</c>
    /// and <c>TIME</c>, and since <c>stock-1.2</c> <c>TIMEFRAME</c> and <c>INSTRUMENT</c>.
    /// </summary>
    public class StockContext : IterationContext
    {
        public const string TimeframeConstant = "TIMEFRAME";
        public const string InstrumentConstant = "INSTRUMENT";

        private static readonly IReadOnlyList<string> StockTypeNames = ["Stock", "Iteration", "Default"];

        internal static IReadOnlyList<string> TypeNamesOf(string name) => [name, .. StockTypeNames];

        internal StockSeries Series { get; }

        /// <summary>
        /// Candles provided by the host application, from the oldest to the newest.
        /// </summary>
        public IReadOnlyList<Candle> Candles => Series.Candles;

        /// <summary>
        /// The <c>TIMEFRAME</c> constant, or <c>null</c>.
        /// </summary>
        public string? Timeframe => Series.Timeframe;

        /// <summary>
        /// The <c>INSTRUMENT</c> constant, or <c>null</c>.
        /// </summary>
        public string? Instrument => Series.Instrument;

        /// <summary>
        /// Context that called <c>LOAD_INDICATOR</c> or <c>LOAD_PATTERN</c>, or <c>null</c> if the host application
        /// created this context.
        /// </summary>
        public IContext? Caller { get; }

        /// <summary>
        /// Key of the computed indicator or pattern (timeframe, identifier, completed parameters and source); <c>null</c>
        /// if the host application created this context.
        /// </summary>
        internal string? FormulaKey { get; }

        public override IReadOnlyList<string> ContextTypeNames => StockTypeNames;

        private protected StockContext(IEnvironment environment, StockSeries series, string? formulaKey = null, IContext? caller = null)
            : base(environment, series.Count)
        {
            Series = series;
            FormulaKey = formulaKey;
            Caller = caller;
        }

        /// <param name="executor">
        /// Executor of the formulas loaded with <c>LOAD_INDICATOR</c> and <c>LOAD_PATTERN</c>; the Core executor by default.
        /// </param>
        /// <exception cref="ArgumentException">A candle breaks the rules of <c>Stock.Candle</c>.</exception>
        public static StockContext Create(IEnvironment environment, IEnumerable<Candle> candles, Executor? executor = null)
            => Prepare(new StockContext(environment, new StockSeries(candles, executor)));

        /// <param name="options">The options of the context: <c>TIMEFRAME</c>, <c>INSTRUMENT</c>, other timeframes.</param>
        /// <exception cref="ArgumentException">
        /// A candle breaks the rules of <c>Stock.Candle</c>, or other timeframes are given without the length of the main
        /// timeframe.
        /// </exception>
        public static StockContext Create(IEnvironment environment, IEnumerable<Candle> candles, StockContextOptions options)
            => Prepare(new StockContext(environment, new StockSeries(candles, options)));

        private protected static T Prepare<T>(T context) where T : StockContext
        {
            context.PreExecute();
            return context;
        }

        internal static StockSeries CreateSeries(IEnumerable<Candle> candles, StockContextOptions? options)
            => new(candles, options ?? new StockContextOptions());

        protected override void LoadContextData()
        {
            base.LoadContextData();

            SetSeriesConstants();
            SetConstant(TimeframeConstant, TextOrNull(Series.Timeframe, TimeframeConstant));
            SetConstant(InstrumentConstant, TextOrNull(Series.Instrument, InstrumentConstant));
        }

        private void SetSeriesConstants()
        {
            SetConstant("CANDLES", Series.CandlesValue);
            SetConstant("OPEN", Series.Open);
            SetConstant("HIGH", Series.High);
            SetConstant("LOW", Series.Low);
            SetConstant("CLOSE", Series.Close);
            SetConstant("VOLUME", Series.Volume);
            SetConstant("TIME", Series.Time);
        }

        private static IValue TextOrNull(string? value, string name)
            => value == null ? Value.Null(name) : Value.Text(value, Core.Utils.StringConverter.TextToLiteral(value));

        /// <summary>
        /// Adds candles that closed after the last one (e.g. on a live chart), so the iterations can be resumed with
        /// <see cref="Executor.ResumeIterations(string, IterationContext, bool)"/> (docs/core/context.md, "Resumed
        /// iterations"). The indicators and patterns loaded by the formula are computed again.
        /// </summary>
        /// <exception cref="ArgumentException">A candle breaks the rules of <c>Stock.Candle</c>.</exception>
        /// <exception cref="InvalidOperationException">A child context created by <c>LOAD_INDICATOR</c> or <c>LOAD_PATTERN</c>.</exception>
        public virtual void AppendCandles(IEnumerable<Candle> candles)
        {
            if (Caller != null)
                throw new InvalidOperationException("The candles of a child context cannot be changed.");

            Series.Append(candles);
            Resize(Series.Count);
            SetSeriesConstants();
        }

        /// <summary>
        /// The count of a Stock Context follows its candles: use <see cref="AppendCandles"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always.</exception>
        public override void Extend(int count)
            => throw new InvalidOperationException("The candles of a Stock Context are added with AppendCandles.");

        /// <summary>
        /// Computes an indicator of the registry for these candles, like <c>LOAD_INDICATOR(id, parameters, source)</c>.
        /// </summary>
        /// <param name="parameters">Parameters of the indicator; missing ones have their default values.</param>
        /// <param name="source">
        /// Input series (<c>N</c> Numbers or <c>null</c> values), <c>CLOSE</c> by default, or an Object of options
        /// (<c>source</c>, <c>timeframe</c>, <c>output</c>, <c>align</c>).
        /// </param>
        /// <returns>An Array, or an Error.</returns>
        public IValue ComputeIndicator(string id, IObject? parameters = null, IValue? source = null)
            => Invoke(StockFunctions.LoadIndicatorName,
            [
                Value.Text(id, ""),
                Value.Object(parameters ?? new Core.Primitives.Object(), ""),
                source ?? Series.Close,
            ]);

        /// <summary>
        /// Checks a pattern of the registry for these candles, like <c>LOAD_PATTERN(id, parameters, options)</c>.
        /// </summary>
        /// <param name="parameters">Parameters of the pattern; missing ones have their default values.</param>
        /// <param name="options">An Object of options (<c>timeframe</c>, <c>align</c>), or <c>null</c>.</param>
        /// <returns>An Array of Booleans, or an Error.</returns>
        public IValue ComputePattern(string id, IObject? parameters = null, IObject? options = null)
        {
            List<IValue> arguments = [Value.Text(id, ""), Value.Object(parameters ?? new Core.Primitives.Object(), "")];

            if (options != null)
                arguments.Add(Value.Object(options, ""));

            return Invoke(StockFunctions.LoadPatternName, [.. arguments]);
        }

        private IValue Invoke(string functionName, IValue[] arguments)
        {
            IFunction? function = Environment.GetFunction(functionName);

            if (function == null)
                return Value.Error(Core.Errors.UnknownSymbolError($"The environment does not support {StockStandard.Identifier}."), "");

            return function.Invoke(arguments, this);
        }
    }

    /// <summary>
    /// <b>Indicator Context</b> (docs/standards/stock/README.md, "Indicator Context"): computes the value of an indicator
    /// for the current candle. Constants: <c>PARAMETERS</c> and <c>SOURCE</c>. Expected result: <c>null</c>, a Number,
    /// or an Object with Number values.
    /// </summary>
    public class IndicatorContext : StockContext
    {
        private static readonly IReadOnlyList<string> IndicatorTypeNames = TypeNamesOf("Stock.Indicator");

        public IValue Parameters { get; }

        public IValue Source { get; private set; }

        public override IReadOnlyList<string> ContextTypeNames => IndicatorTypeNames;

        internal IndicatorContext(IEnvironment environment, StockSeries series, IValue parameters, IValue source,
            string? indicatorKey, IContext? caller)
            : base(environment, series, indicatorKey, caller)
        {
            Parameters = parameters;
            Source = source;
        }

        /// <summary>
        /// Creates an Indicator Context for a formula that is not in the registry, e.g. to test it. Run it with
        /// <see cref="StockExecutor.ExecuteIndicator(Executor, string, IndicatorContext)"/>.
        /// </summary>
        /// <param name="parameters">The <c>PARAMETERS</c> object (not completed with default values).</param>
        /// <param name="source">The <c>SOURCE</c> series; the closing prices by default.</param>
        public static IndicatorContext Create(IEnvironment environment, IEnumerable<Candle> candles,
            IObject? parameters = null, IEnumerable<double?>? source = null, Executor? executor = null)
            => Create(environment, candles, new StockContextOptions { Executor = executor }, parameters, source);

        /// <inheritdoc cref="Create(IEnvironment, IEnumerable{Candle}, IObject?, IEnumerable{double?}?, Executor?)"/>
        /// <param name="options">The options of the context: <c>TIMEFRAME</c>, <c>INSTRUMENT</c>, other timeframes.</param>
        public static IndicatorContext Create(IEnvironment environment, IEnumerable<Candle> candles, StockContextOptions options,
            IObject? parameters = null, IEnumerable<double?>? source = null)
        {
            StockSeries series = CreateSeries(candles, options);
            IValue sourceValue = source == null ? series.Close : SourceValue(source);

            return Prepare(new IndicatorContext(environment, series,
                Value.Object(parameters ?? new Core.Primitives.Object(), "PARAMETERS"), sourceValue, null, null));
        }

        private static IValue SourceValue(IEnumerable<double?> values)
            => Value.Array(values.Select(v => v is double d ? (IValue)Value.Number(d) : Value.Null("null")).ToArray(), "SOURCE");

        protected override void LoadContextData()
        {
            base.LoadContextData();

            SetConstant("PARAMETERS", Parameters);
            SetConstant("SOURCE", Source);
        }

        /// <summary>
        /// Adds candles, as <see cref="StockContext.AppendCandles"/>. <c>SOURCE</c> follows <c>CLOSE</c>; use
        /// <see cref="AppendCandles(IEnumerable{Candle}, IEnumerable{double?}?)"/> for a <c>SOURCE</c> given by the host
        /// application.
        /// </summary>
        public override void AppendCandles(IEnumerable<Candle> candles) => AppendCandles(candles, null);

        /// <summary>
        /// Adds candles, as <see cref="StockContext.AppendCandles"/>. <c>SOURCE</c> follows <c>CLOSE</c>; a <c>SOURCE</c>
        /// given by the host application gets the values of the new candles from <paramref name="source"/> (one Number or
        /// <c>null</c> for every new candle).
        /// </summary>
        /// <exception cref="ArgumentException">A <c>SOURCE</c> given by the host application gets another number of values.</exception>
        public void AppendCandles(IEnumerable<Candle> candles, IEnumerable<double?>? source)
        {
            Candle[] added = candles.ToArray();
            bool followsClose = ReferenceEquals(Source, Series.Close);
            double?[]? values = source?.ToArray();

            if (!followsClose && values?.Length != added.Length)
                throw new ArgumentException("The SOURCE of the context needs one value for every new candle.", nameof(source));

            base.AppendCandles(added);

            Source = followsClose
                ? Series.Close
                : Value.Array(((IValue[])Source.Raw!).Concat(((IValue[])SourceValue(values!).Raw!)).ToArray(), "SOURCE");

            SetConstant("SOURCE", Source);
        }
    }

    /// <summary>
    /// <b>Pattern Context</b> (docs/standards/stock/README.md, "Pattern Context"): the formula returns <c>true</c> if the
    /// pattern is found at the current candle. Constant: <c>PARAMETERS</c>. Run it with
    /// <see cref="StockExecutor.ExecutePattern(Executor, string, PatternContext, bool)"/>.
    /// </summary>
    public class PatternContext : StockContext
    {
        private static readonly IReadOnlyList<string> PatternTypeNames = TypeNamesOf("Stock.Pattern");

        public IValue Parameters { get; }

        public override IReadOnlyList<string> ContextTypeNames => PatternTypeNames;

        internal PatternContext(IEnvironment environment, StockSeries series, IValue parameters, string? patternKey, IContext? caller)
            : base(environment, series, patternKey, caller)
        {
            Parameters = parameters;
        }

        /// <param name="parameters">The <c>PARAMETERS</c> object (not completed with default values); <c>{ }</c> by default.</param>
        /// <exception cref="ArgumentException">A candle breaks the rules of <c>Stock.Candle</c>.</exception>
        public static PatternContext Create(IEnvironment environment, IEnumerable<Candle> candles, Executor? executor = null,
            IObject? parameters = null)
            => Create(environment, candles, new StockContextOptions { Executor = executor }, parameters);

        /// <inheritdoc cref="Create(IEnvironment, IEnumerable{Candle}, Executor?, IObject?)"/>
        /// <param name="options">The options of the context: <c>TIMEFRAME</c>, <c>INSTRUMENT</c>, other timeframes.</param>
        public static PatternContext Create(IEnvironment environment, IEnumerable<Candle> candles, StockContextOptions options,
            IObject? parameters = null)
            => Prepare(new PatternContext(environment, CreateSeries(candles, options),
                Value.Object(parameters ?? new Core.Primitives.Object(), "PARAMETERS"), null, null));

        protected override void LoadContextData()
        {
            base.LoadContextData();

            SetConstant("PARAMETERS", Parameters);
        }
    }

    /// <summary>
    /// <b>Strategy Context</b> (docs/standards/stock/README.md, "Strategy Context"): the formula returns a Number from
    /// <c>SELL</c> (-1) to <c>BUY</c> (1), or since <c>stock-1.2</c> a signal object
    /// <c>{ signal, reason?, checks?, ... }</c>. Constants: <c>BUY</c>, <c>NEUTRAL</c>, <c>SELL</c> and, since
    /// <c>stock-1.2</c>, <c>PARAMETERS</c>. Run it with <see cref="StockExecutor.ExecuteStrategy(Executor, string, StrategyContext, bool)"/>.
    /// </summary>
    public class StrategyContext : StockContext
    {
        public const double Buy = 1;
        public const double Neutral = 0;
        public const double Sell = -1;

        private static readonly IReadOnlyList<string> StrategyTypeNames = TypeNamesOf("Stock.Strategy");

        /// <summary>
        /// The <c>PARAMETERS</c> constant.
        /// </summary>
        public IValue Parameters { get; private set; }

        public override IReadOnlyList<string> ContextTypeNames => StrategyTypeNames;

        private protected StrategyContext(IEnvironment environment, StockSeries series, IValue parameters) : base(environment, series)
        {
            Parameters = parameters;
        }

        /// <exception cref="ArgumentException">A candle breaks the rules of <c>Stock.Candle</c>.</exception>
        public static new StrategyContext Create(IEnvironment environment, IEnumerable<Candle> candles, Executor? executor = null)
            => Create(environment, candles, new StockContextOptions { Executor = executor });

        /// <param name="options">The options of the context: <c>TIMEFRAME</c>, <c>INSTRUMENT</c>, other timeframes.</param>
        /// <param name="parameters">
        /// The <c>PARAMETERS</c> object (since <c>stock-1.2</c>); <c>{ }</c> by default. Complete it with the <c>default</c>
        /// values of the metadata with <see cref="CompleteParameters"/>.
        /// </param>
        /// <exception cref="ArgumentException">A candle breaks the rules of <c>Stock.Candle</c>.</exception>
        public static StrategyContext Create(IEnvironment environment, IEnumerable<Candle> candles, StockContextOptions options,
            IObject? parameters = null)
            => Prepare(new StrategyContext(environment, CreateSeries(candles, options),
                Value.Object(parameters ?? new Core.Primitives.Object(), "PARAMETERS")));

        /// <summary>
        /// Completes <c>PARAMETERS</c> with the <c>default</c> values of the <c>parameters</c> of the metadata of the
        /// strategy (docs/standards/stock/README.md, "Strategy Context"): a parameter that the host application did not
        /// give gets its default value. A parameter without a default stays missing.
        /// </summary>
        public void CompleteParameters(IValue metadata)
        {
            var given = (IObject)Parameters.Raw!;
            var missing = ParameterDefaults(metadata).Where(p => !given.HasKey(p.Key)).ToArray();

            if (missing.Length == 0)
                return;

            var completed = new Core.Primitives.Object();

            foreach (string key in given.Keys)
                completed.InitVariable(key, given.GetProperty(key).GetValue());

            foreach (var (name, value) in missing)
            {
                if (value != null)
                    completed.InitVariable(name, value);
            }

            Parameters = Value.Object(completed, "PARAMETERS");
            SetConstant("PARAMETERS", Parameters);
        }

        protected override void LoadContextData()
        {
            base.LoadContextData();

            SetConstant("BUY", Value.Number(Buy, "BUY"));
            SetConstant("NEUTRAL", Value.Number(Neutral, "NEUTRAL"));
            SetConstant("SELL", Value.Number(Sell, "SELL"));
            SetConstant("PARAMETERS", Parameters);
        }

        /// <summary>
        /// An Object result is a signal object: it needs a Number <c>signal</c>; <c>reason</c> is a Text and <c>checks</c>
        /// an Object of Booleans. Another Object is an <c>OPERATION_ERROR</c> of the iteration. Other results are checked
        /// by <see cref="StockExecutor"/>.
        /// </summary>
        public override IValue FinalizeResult(IValue value)
        {
            if (value.Type.Base != Types.ObjectName || value.Raw is not IObject obj)
                return value;

            return SignalObjectProblem(obj) is string problem ? Value.Error(Core.Errors.OperationError(problem), "") : value;
        }

        /// <summary>
        /// Why an Object is not a signal object, or <c>null</c> if it is one.
        /// </summary>
        public static string? SignalObjectProblem(IObject obj)
        {
            IValue? Read(string key) => obj.HasKey(key) ? obj.GetProperty(key).GetValue() : null;

            if (Read("signal") is not IValue signal)
                return "A signal object must have the key \"signal\".";

            if (signal.Type.Base != Types.NumberName)
                return "The \"signal\" of a signal object must be a Number.";

            if (Read("reason") is IValue reason && reason.Type.Base != Types.TextName)
                return "The \"reason\" of a signal object must be a Text.";

            if (Read("checks") is IValue checks
                && (checks.Raw is not IObject checksObject || checks.Type.Base != Types.ObjectName
                    || checksObject.Keys.Any(key => checksObject.GetProperty(key).GetValue().Type.Base != Types.BooleanName)))
                return "The \"checks\" of a signal object must be an Object of Boolean values.";

            return null;
        }

        /// <summary>
        /// The parameters of the metadata of a formula with their <c>default</c> values (<c>null</c> without a default),
        /// in the order of the metadata.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, IValue?>> ParameterDefaults(IValue metadata)
        {
            var defaults = new List<KeyValuePair<string, IValue?>>();

            if (metadata.Raw is not IObject obj || !obj.HasKey("parameters")
                || obj.GetProperty("parameters").GetValue() is not { Raw: IObject parameters } value || value.Type.Base != Types.ObjectName)
                return defaults;

            foreach (string name in parameters.Keys)
            {
                IValue description = parameters.GetProperty(name).GetValue();
                IValue? defaultValue = description.Type.Base == Types.ObjectName && description.Raw is IObject d && d.HasKey("default")
                    ? d.GetProperty("default").GetValue()
                    : null;

                defaults.Add(new KeyValuePair<string, IValue?>(name, defaultValue));
            }

            return defaults;
        }
    }
}
