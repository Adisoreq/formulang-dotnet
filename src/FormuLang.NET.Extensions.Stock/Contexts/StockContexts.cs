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
    /// and <c>TIME</c>.
    /// </summary>
    public class StockContext : IterationContext
    {
        internal StockSeries Series { get; }

        /// <summary>
        /// Candles provided by the host application, from the oldest to the newest.
        /// </summary>
        public IReadOnlyList<Candle> Candles => Series.Candles;

        /// <summary>
        /// Context that called <c>LOAD_INDICATOR</c> or <c>LOAD_PATTERN</c>, or <c>null</c> if the host application
        /// created this context.
        /// </summary>
        public IContext? Caller { get; }

        /// <summary>
        /// Key of the computed indicator or pattern (identifier, completed parameters and source); <c>null</c> if the
        /// host application created this context.
        /// </summary>
        internal string? FormulaKey { get; }

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

        private protected static T Prepare<T>(T context) where T : StockContext
        {
            context.PreExecute();
            return context;
        }

        protected override void LoadContextData()
        {
            base.LoadContextData();

            SetConstant("CANDLES", Series.CandlesValue);
            SetConstant("OPEN", Series.Open);
            SetConstant("HIGH", Series.High);
            SetConstant("LOW", Series.Low);
            SetConstant("CLOSE", Series.Close);
            SetConstant("VOLUME", Series.Volume);
            SetConstant("TIME", Series.Time);
        }

        /// <summary>
        /// Computes an indicator of the registry for these candles, like <c>LOAD_INDICATOR(id, parameters, source)</c>.
        /// </summary>
        /// <param name="parameters">Parameters of the indicator; missing ones have their default values.</param>
        /// <param name="source">Input series (<c>N</c> Numbers or <c>null</c> values); <c>CLOSE</c> by default.</param>
        /// <returns>An Array of <c>N</c> values, or an Error.</returns>
        public IValue ComputeIndicator(string id, IObject? parameters = null, IValue? source = null)
            => Invoke(StockFunctions.LoadIndicatorName,
            [
                Value.Text(id, ""),
                Value.Object(parameters ?? new Core.Primitives.Object(), ""),
                source ?? Series.Close,
            ]);

        /// <summary>
        /// Checks a pattern of the registry for these candles, like <c>LOAD_PATTERN(id, parameters)</c>.
        /// </summary>
        /// <param name="parameters">Parameters of the pattern; missing ones have their default values.</param>
        /// <returns>An Array of <c>N</c> Booleans, or an Error.</returns>
        public IValue ComputePattern(string id, IObject? parameters = null)
            => Invoke(StockFunctions.LoadPatternName,
            [
                Value.Text(id, ""),
                Value.Object(parameters ?? new Core.Primitives.Object(), ""),
            ]);

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
        public IValue Parameters { get; }

        public IValue Source { get; }

        internal IndicatorContext(IEnvironment environment, StockSeries series, IValue parameters, IValue source,
            string? indicatorKey, IContext? caller)
            : base(environment, series, indicatorKey, caller)
        {
            Parameters = parameters;
            Source = source;
        }

        /// <summary>
        /// Creates an Indicator Context for a formula that is not in the registry, e.g. to test it. Run it with
        /// <see cref="StockExecutor.ExecuteIndicator"/>.
        /// </summary>
        /// <param name="parameters">The <c>PARAMETERS</c> object (not completed with default values).</param>
        /// <param name="source">The <c>SOURCE</c> series; the closing prices by default.</param>
        public static IndicatorContext Create(IEnvironment environment, IEnumerable<Candle> candles,
            IObject? parameters = null, IEnumerable<double?>? source = null, Executor? executor = null)
        {
            var series = new StockSeries(candles, executor);

            IValue sourceValue = source == null
                ? series.Close
                : Value.Array(source.Select(v => v is double d ? (IValue)Value.Number(d, "") : Value.Null("null")), "SOURCE");

            return Prepare(new IndicatorContext(environment, series,
                Value.Object(parameters ?? new Core.Primitives.Object(), "PARAMETERS"), sourceValue, null, null));
        }

        protected override void LoadContextData()
        {
            base.LoadContextData();

            SetConstant("PARAMETERS", Parameters);
            SetConstant("SOURCE", Source);
        }
    }

    /// <summary>
    /// <b>Pattern Context</b> (docs/standards/stock/README.md, "Pattern Context"): the formula returns <c>true</c> if the
    /// pattern is found at the current candle. Constant: <c>PARAMETERS</c>. Run it with
    /// <see cref="StockExecutor.ExecutePattern"/>.
    /// </summary>
    public class PatternContext : StockContext
    {
        public IValue Parameters { get; }

        internal PatternContext(IEnvironment environment, StockSeries series, IValue parameters, string? patternKey, IContext? caller)
            : base(environment, series, patternKey, caller)
        {
            Parameters = parameters;
        }

        /// <param name="parameters">The <c>PARAMETERS</c> object (not completed with default values); <c>{ }</c> by default.</param>
        /// <exception cref="ArgumentException">A candle breaks the rules of <c>Stock.Candle</c>.</exception>
        public static PatternContext Create(IEnvironment environment, IEnumerable<Candle> candles, Executor? executor = null,
            IObject? parameters = null)
            => Prepare(new PatternContext(environment, new StockSeries(candles, executor),
                Value.Object(parameters ?? new Core.Primitives.Object(), "PARAMETERS"), null, null));

        protected override void LoadContextData()
        {
            base.LoadContextData();

            SetConstant("PARAMETERS", Parameters);
        }
    }

    /// <summary>
    /// <b>Strategy Context</b> (docs/standards/stock/README.md, "Strategy Context"): the formula returns a Number from
    /// <c>SELL</c> (-1) to <c>BUY</c> (1). Run it with <see cref="StockExecutor.ExecuteStrategy"/>.
    /// </summary>
    public class StrategyContext : StockContext
    {
        public const double Buy = 1;
        public const double Neutral = 0;
        public const double Sell = -1;

        private protected StrategyContext(IEnvironment environment, StockSeries series) : base(environment, series) { }

        /// <exception cref="ArgumentException">A candle breaks the rules of <c>Stock.Candle</c>.</exception>
        public static new StrategyContext Create(IEnvironment environment, IEnumerable<Candle> candles, Executor? executor = null)
            => Prepare(new StrategyContext(environment, new StockSeries(candles, executor)));

        protected override void LoadContextData()
        {
            base.LoadContextData();

            SetConstant("BUY", Value.Number(Buy, "BUY"));
            SetConstant("NEUTRAL", Value.Number(Neutral, "NEUTRAL"));
            SetConstant("SELL", Value.Number(Sell, "SELL"));
        }
    }
}
