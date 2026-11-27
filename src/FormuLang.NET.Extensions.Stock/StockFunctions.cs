using FormuLang.NET.Abstractions.Functions;
using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core;
using FormuLang.NET.Core.Functions;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Utils;
using FormuLang.NET.Extensions.Stock.Contexts;
using FormuLang.NET.Extensions.Stock.Registry;

namespace FormuLang.NET.Extensions.Stock
{
    /// <summary>
    /// Built-in functions of the Stock standard (docs/standards/stock/functions).
    /// </summary>
    public static class StockFunctions
    {
        public const string CandleName = "CANDLE";
        public const string IsBullishName = "IS_BULLISH";
        public const string IsBearishName = "IS_BEARISH";
        public const string LoadIndicatorName = "LOAD_INDICATOR";
        public const string LoadPatternName = "LOAD_PATTERN";

        /// <summary>
        /// <c>CANDLE(object)</c> (docs/standards/stock/functions/CANDLE.md).
        /// </summary>
        public static Function Candle { get; } = new(CandleName,
        [
            new FunctionOverload([ArgumentTemplate.Of("object", Types.Object)], (a, _) =>
                Stock.Candle.TryFromObject((IObject)a[0].Raw!, out Candle candle)
                    ? candle.ToValue()
                    : OperationError($"Object does not meet the requirements to be <{Stock.Candle.TypeName}>.")),
        ]) { SideEffectFree = true };

        /// <summary>
        /// <c>IS_BULLISH(candle)</c>: <c>close &gt; open</c> (docs/standards/stock/functions/IS_BULLISH.md).
        /// </summary>
        public static Function IsBullish { get; } = Direction(IsBullishName, (open, close) => close > open);

        /// <summary>
        /// <c>IS_BEARISH(candle)</c>: <c>close &lt; open</c> (docs/standards/stock/functions/IS_BEARISH.md).
        /// </summary>
        public static Function IsBearish { get; } = Direction(IsBearishName, (open, close) => close < open);

        /// <summary>
        /// <c>LOAD_INDICATOR(id, parameters, source)</c> with the indicators of <paramref name="registry"/>
        /// (docs/standards/stock/functions/LOAD_INDICATOR.md).
        /// </summary>
        public static LoadIndicatorFunction LoadIndicator(FormulaRegistry registry) => new(registry);

        /// <summary>
        /// <c>LOAD_PATTERN(id, parameters)</c> with the patterns of <paramref name="registry"/>
        /// (docs/standards/stock/functions/LOAD_PATTERN.md).
        /// </summary>
        public static LoadPatternFunction LoadPattern(FormulaRegistry registry) => new(registry);

        private static Function Direction(string name, Func<double, double, bool> check) => new(name,
        [
            new FunctionOverload([ArgumentTemplate.Of("candle", Types.Of(Types.ObjectName, Stock.Candle.TypeName))], (a, _) =>
            {
                var candle = (IObject)a[0].Raw!;
                double open = (double)candle.GetProperty("open").GetValue().Raw!;
                double close = (double)candle.GetProperty("close").GetValue().Raw!;
                bool result = check(open, close);
                return Value.Boolean(result, result ? "true" : "false");
            }),
        ]) { SideEffectFree = true };

        internal static IValue OperationError(string message) => Value.Error(Errors.OperationError(message), "");

        internal static IValue InvalidKeyError(string message) => Value.Error(Errors.InvalidKeyError(message), "");

        internal static bool IsError(IValue value) => value.Type.Base == Types.ErrorName;

        /// <summary>
        /// Steps shared by <c>LOAD_INDICATOR</c> and <c>LOAD_PATTERN</c>: finds the definition and completes the
        /// parameters with the default values.
        /// </summary>
        /// <returns><c>null</c> if the formula can be executed, or the Error.</returns>
        internal static IValue? PrepareLoad(FormulaCollection part, string id, IObject? parameters,
            out FormulaDefinition definition, out IValue completedParameters)
        {
            definition = null!;
            completedParameters = null!;

            // 1. Definition
            if (part.Get(id) is not FormulaDefinition found)
                return InvalidKeyError($"{part.Kind} \"{id}\" is not defined.");

            definition = found;

            // 2. Parameters, completed with the default values
            if (parameters != null)
            {
                foreach (string name in parameters.Keys)
                {
                    if (definition.GetParameter(name) == null)
                        return InvalidKeyError($"{part.Kind} \"{id}\" has no parameter \"{name}\".");
                }
            }

            var completed = new Core.Primitives.Object();

            foreach (FormulaParameter parameter in definition.Parameters)
            {
                IValue value = parameters != null && parameters.HasKey(parameter.Name)
                    ? parameters.GetProperty(parameter.Name).GetValue()
                    : parameter.DefaultValue;

                completed.InitConstant(parameter.Name, value);
            }

            completedParameters = Value.Object(completed, "PARAMETERS");
            return null;
        }

        // Options of LOAD_INDICATOR, LOAD_PATTERN and LOAD_CANDLES (stock-1.2)

        /// <summary>
        /// The options by name, or the <c>ARGUMENT_ERROR</c> of an unknown option or an option of a wrong type.
        /// </summary>
        internal static IValue? ReadOptions(string functionName, IObject options, IReadOnlyDictionary<string, string> types,
            out Dictionary<string, IValue> values)
        {
            values = new Dictionary<string, IValue>(StringComparer.Ordinal);

            foreach (string key in options.Keys)
            {
                if (!types.TryGetValue(key, out string? type))
                    return Value.Error(Errors.ArgumentError($"\"{functionName}\" has no option \"{key}\"."), "");

                IValue value = options.GetProperty(key).GetValue();

                if (value.Type.Base != type)
                    return Value.Error(Errors.ArgumentError($"The option \"{key}\" of \"{functionName}\" must be of type {type}."), "");

                values[key] = value;
            }

            return null;
        }

        /// <summary>
        /// The series of another timeframe, or the <c>NOT_SUPPORTED_ERROR</c> of a timeframe that the host does not provide.
        /// </summary>
        internal static IValue? TimeframeSeries(StockContext caller, string timeframe, out StockSeries? series)
        {
            series = caller.Series.OtherTimeframe(timeframe);
            return series == null
                ? Value.Error(Errors.NotSupportedError($"The timeframe \"{timeframe}\" is not provided by the host application."), "")
                : null;
        }

        /// <summary>
        /// Key of a computed series in the store of its series: the timeframe, the kind and the identifier.
        /// </summary>
        internal static string SeriesKey(StockSeries series, string kind, string rest) => $"{series.Timeframe ?? ""}|{kind}|{rest}";

        /// <summary>
        /// The series of another timeframe seen from the current candle of the caller (docs/standards/stock/README.md,
        /// "Aligned and native series"): aligned, <c>N</c> elements, the element of the last candle closed at every candle
        /// (<c>null</c> before the first one); or native, the elements of the candles closed at the current candle. The
        /// aligned series is computed once and stored.
        /// </summary>
        internal static IValue FromTimeframe(StockContext caller, StockSeries target, IValue native, bool align, string key)
        {
            if (ReferenceEquals(target, caller.Series) && align)
                return native;

            IValue[] elements = (IValue[])native.Raw!;
            int[] alignment = caller.Series.Alignment(target);

            if (!align)
                return Value.Array(elements.Take(alignment[caller.Iteration] + 1).ToArray(), "");

            string alignedKey = $"aligned|{target.Timeframe ?? ""}|{key}";

            if (caller.Series.TryGetStored(alignedKey, out IValue? stored))
                return stored!;

            IValue aligned = Value.Array(alignment.Select(j => j < 0 ? Value.Null("null") : elements[j]).ToArray(), "");
            caller.Series.Store(alignedKey, aligned);
            return aligned;
        }

        // LOAD_CANDLES (stock-1.2)

        public const string LoadCandlesName = "LOAD_CANDLES";

        /// <summary>
        /// <c>LOAD_CANDLES(timeframe, options?)</c> (docs/standards/stock/functions/LOAD_CANDLES.md): the candles of
        /// another timeframe, aligned with the main chart or native (<c>align: false</c>).
        /// </summary>
        public static Function LoadCandles { get; } = new(LoadCandlesName,
        [
            new FunctionOverload([ArgumentTemplate.Of("timeframe", Types.Text)], (a, c) => ComputeLoadCandles(c, (string)a[0].Raw!, null)),
            new FunctionOverload([ArgumentTemplate.Of("timeframe", Types.Text), ArgumentTemplate.Of("options", Types.Object)],
                (a, c) => ComputeLoadCandles(c, (string)a[0].Raw!, (IObject)a[1].Raw!)),
        ]) { SideEffectFree = true };

        private static IValue ComputeLoadCandles(IContext context, string timeframe, IObject? options)
        {
            if (context is not StockContext caller)
                return OperationError($"\"{LoadCandlesName}\" can be called only in a Stock Context.");

            bool align = true;

            if (options != null)
            {
                if (ReadOptions(LoadCandlesName, options, new Dictionary<string, string> { ["align"] = Types.BooleanName }, out var read) is IValue error)
                    return error;

                align = read.GetValueOrDefault("align")?.Raw is not false;
            }

            if (TimeframeSeries(caller, timeframe, out StockSeries? target) is IValue missing)
                return missing;

            return FromTimeframe(caller, target!, target!.CandlesValue, align, "candles");
        }

        /// <summary>
        /// Checks that the formula with <paramref name="key"/> is not already being computed by one of the calling
        /// formulas (the formula calls itself, directly or indirectly).
        /// </summary>
        internal static bool CallsItself(StockContext caller, string key)
        {
            for (IContext? c = caller; c is StockContext stock; c = stock.Caller)
            {
                if (stock.FormulaKey == key)
                    return true;
            }

            return false;
        }

        internal static string ParametersKey(IValue parameters) => StringConverter.ValueToString(parameters);

        // stock-1.1

        public const string LagName = "LAG";
        public const string WindowName = "WINDOW";

        /// <summary>
        /// <c>LAG(series, offset)</c> (docs/standards/stock/functions/LAG.md): the value <c>offset</c> candles before the
        /// current one (1 by default), or <c>null</c> before the first candle.
        /// </summary>
        public static Function Lag { get; } = new(LagName,
        [
            new FunctionOverload([ArgumentTemplate.Of("series", Types.Array)], (a, c) => ComputeLag((IValue[])a[0].Raw!, 1, c)),
            new FunctionOverload([ArgumentTemplate.Of("series", Types.Array), ArgumentTemplate.Of("offset", Types.Number)],
                (a, c) => ComputeLag((IValue[])a[0].Raw!, (double)a[1].Raw!, c)),
        ]) { SideEffectFree = true };

        /// <summary>
        /// <c>WINDOW(series, length, handler?)</c> (docs/standards/stock/functions/WINDOW.md): the last <c>length</c> values
        /// up to the current candle, or the result of <c>handler</c> for them; <c>null</c> while there are fewer values.
        /// </summary>
        public static Function Window { get; } = new(WindowName,
        [
            new FunctionOverload([ArgumentTemplate.Of("series", Types.Array), ArgumentTemplate.Of("length", Types.Number)],
                (a, c) => ComputeWindow((IValue[])a[0].Raw!, (double)a[1].Raw!, null, c)),
            new FunctionOverload([ArgumentTemplate.Of("series", Types.Array), ArgumentTemplate.Of("length", Types.Number), ArgumentTemplate.Of("handler", Types.Delegate)],
                (a, c) => ComputeWindow((IValue[])a[0].Raw!, (double)a[1].Raw!, (IFunction)a[2].Raw!, c)),
        ]) { SideEffectFree = true };

        /// <summary>
        /// Position of the current candle in a series of a Stock Context (independent of <c>@array_indexing</c>), or an Error.
        /// </summary>
        internal static IValue? CurrentPosition(string name, IValue[] series, IContext context, out int position)
        {
            position = 0;

            if (context is not StockContext stock)
                return OperationError($"\"{name}\" can be called only in a Stock Context.");

            if (series.Length != stock.Count)
                return OperationError($"The series has {series.Length} elements, but there are {stock.Count} candles.");

            position = stock.Iteration;
            return null;
        }

        private static IValue ComputeLag(IValue[] series, double offset, IContext context)
        {
            if (CurrentPosition(LagName, series, context, out int position) is IValue error)
                return error;

            if (!double.IsFinite(offset) || Math.Floor(offset) != offset || offset < 0)
                return OperationError("The offset must be a whole number of 0 or more (a future value cannot be read).");

            double at = position - offset;
            return at < 0 ? Value.Null("null") : series[(int)at];
        }

        private static IValue ComputeWindow(IValue[] series, double length, IFunction? handler, IContext context)
        {
            if (CurrentPosition(WindowName, series, context, out int position) is IValue error)
                return error;

            if (!double.IsFinite(length) || Math.Floor(length) != length || length < 1)
                return OperationError("The length of a window must be a whole number of 1 or more.");

            if (position + 1 < length)
                return Value.Null("null");

            int count = (int)length;
            IValue window = Value.Array(series.Skip(position - count + 1).Take(count), "");

            if (handler == null)
                return window;

            if (handler is ILambda lambda && lambda.Parameters.Count != 1)
                return Value.Error(Errors.ArgumentError($"The lambda passed to \"{WindowName}\" must have exactly 1 parameter."), "");

            return handler.Invoke([window], context);
        }
    }

    /// <summary>
    /// The <c>LOAD_INDICATOR</c> function; holds the formula registry of the environment.
    /// <c>LOAD_INDICATOR(id, parameters?, source?)</c> and, since <c>stock-1.2</c>, <c>LOAD_INDICATOR(id, parameters,
    /// options)</c> (docs/standards/stock/functions/LOAD_INDICATOR.md): computes an indicator of the registry for every
    /// candle (of the main chart or of another timeframe) in a child Indicator Context. Computed series are stored by
    /// timeframe, identifier, completed parameters and source; <c>output</c> and <c>align</c> only take a part of a
    /// stored series.
    /// </summary>
    public sealed class LoadIndicatorFunction : Function
    {
        public FormulaRegistry Registry { get; }

        internal LoadIndicatorFunction(FormulaRegistry registry)
            : base(StockFunctions.LoadIndicatorName, CreateOverloads(registry))
        {
            // Computing a series changes nothing for the caller; the stored series are only reused
            SideEffectFree = true;
            Registry = registry;
        }

        private sealed record Options(IValue? Source, string? Timeframe, string? Output, bool Align);

        private static readonly Options Plain = new(null, null, null, true);

        private static IFunctionOverload[] CreateOverloads(FormulaRegistry registry)
        {
            ArgumentTemplate id = ArgumentTemplate.Of("id", Types.Text);
            ArgumentTemplate parameters = ArgumentTemplate.Of("parameters", Types.Object);
            ArgumentTemplate source = ArgumentTemplate.Of("source", Types.Array);
            ArgumentTemplate options = ArgumentTemplate.Of("options", Types.Object);

            return
            [
                new FunctionOverload([id], (a, c) => Load(registry, c, (string)a[0].Raw!, null, Plain)),
                new FunctionOverload([id, parameters], (a, c) => Load(registry, c, (string)a[0].Raw!, (IObject)a[1].Raw!, Plain)),
                new FunctionOverload([id, parameters, source], (a, c) => Load(registry, c, (string)a[0].Raw!, (IObject)a[1].Raw!, Plain with { Source = a[2] })),
                // stock-1.2: options instead of the source
                new FunctionOverload([id, parameters, options], (a, c) =>
                {
                    if (StockFunctions.ReadOptions(StockFunctions.LoadIndicatorName, (IObject)a[2].Raw!, new Dictionary<string, string>
                        {
                            ["source"] = Types.ArrayName,
                            ["timeframe"] = Types.TextName,
                            ["output"] = Types.TextName,
                            ["align"] = Types.BooleanName,
                        }, out var read) is IValue error)
                        return error;

                    return Load(registry, c, (string)a[0].Raw!, (IObject)a[1].Raw!, new Options(
                        read.GetValueOrDefault("source"),
                        read.GetValueOrDefault("timeframe")?.Raw as string,
                        read.GetValueOrDefault("output")?.Raw as string,
                        read.GetValueOrDefault("align")?.Raw is not false));
                }),
            ];
        }

        private static IValue Load(FormulaRegistry registry, IContext context, string id, IObject? parameters, Options options)
        {
            if (context is not StockContext caller)
                return StockFunctions.OperationError($"\"{StockFunctions.LoadIndicatorName}\" can be called only in a Stock Context.");

            // 1. The candles: of the main chart, or of another timeframe
            StockSeries target = caller.Series;

            if (options.Timeframe != null)
            {
                if (StockFunctions.TimeframeSeries(caller, options.Timeframe, out StockSeries? other) is IValue missing)
                    return missing;

                target = other!;
            }

            // 2. Definition and parameters
            if (StockFunctions.PrepareLoad(registry.Indicators, id, parameters, out FormulaDefinition definition, out IValue parametersValue) is IValue error)
                return error;

            // 3. Source
            IValue sourceValue = options.Source ?? target.Close;
            IValue[] elements = (IValue[])sourceValue.Raw!;

            if (elements.Length != target.Count)
            {
                string of = ReferenceEquals(target, caller.Series) ? "" : $" of the timeframe \"{target.Timeframe}\"";
                return StockFunctions.OperationError($"The source has {elements.Length} elements, but there are {target.Count} candles{of}.");
            }

            if (!target.IsValidSource(elements))
                return StockFunctions.OperationError("Every element of the source must be a Number or null.");

            string key = StockFunctions.SeriesKey(target, "indicator",
                $"{id}|{StockFunctions.ParametersKey(parametersValue)}|{target.SourceKey(elements)}");

            // 4-7. The native series of the whole timeframe, then a part of it
            IValue native = Series(caller, target, definition, parametersValue, sourceValue, key);
            if (StockFunctions.IsError(native))
                return native;

            string outputKey = key;

            if (options.Output != null)
            {
                native = Output(target, key, native, options.Output);
                if (StockFunctions.IsError(native))
                    return native;

                outputKey = $"{key}|output={options.Output}";
            }

            return options.Timeframe == null ? native : StockFunctions.FromTimeframe(caller, target, native, options.Align, outputKey);
        }

        /// <summary>
        /// The stored series of the indicator, or the series computed in a child Indicator Context.
        /// </summary>
        private static IValue Series(StockContext caller, StockSeries target, FormulaDefinition definition, IValue parameters,
            IValue source, string key)
        {
            // An indicator that calls itself, directly or indirectly
            if (StockFunctions.CallsItself(caller, key))
                return StockFunctions.OperationError($"Indicator \"{definition.Id}\" calls itself.");

            if (target.TryGetStored(key, out IValue? stored))
                return stored!;

            // Child Indicator Context with its own iterations
            var child = new IndicatorContext(caller.Environment, target, parameters, source, key, caller);
            child.FixedNow = caller.Now;
            child.PreExecute();

            IValue result = target.Executor.ExecuteIndicator(definition.Formula, child);
            target.Store(key, result);
            return result;
        }

        /// <summary>
        /// The series of one output of an indicator: <c>e[output]</c> for every Object result <c>e</c> (<c>null</c> stays
        /// <c>null</c>), or the series unchanged for the output <c>value</c> of Number results. A result without the
        /// output is an <c>INVALID_KEY_ERROR</c>.
        /// </summary>
        private static IValue Output(StockSeries target, string key, IValue series, string output)
        {
            string outputKey = $"{key}|output={output}";

            if (target.TryGetStored(outputKey, out IValue? stored))
                return stored!;

            var elements = new List<IValue>();

            foreach (IValue element in (IValue[])series.Raw!)
            {
                switch (element.Type.Base)
                {
                    case Types.NullName:
                        elements.Add(element);
                        break;

                    case Types.ObjectName:
                        var obj = (IObject)element.Raw!;
                        if (!obj.HasKey(output))
                            return StockFunctions.InvalidKeyError($"A result of the indicator has no key \"{output}\".");

                        elements.Add(obj.GetProperty(output).GetValue());
                        break;

                    default:
                        if (output != "value")
                            return StockFunctions.InvalidKeyError($"The results of the indicator are Numbers: the only output is \"value\", not \"{output}\".");

                        elements.Add(element);
                        break;
                }
            }

            IValue result = Value.Array(elements, "");
            target.Store(outputKey, result);
            return result;
        }
    }

    /// <summary>
    /// The <c>LOAD_PATTERN</c> function; holds the formula registry of the environment.
    /// <c>LOAD_PATTERN(id, parameters?, options?)</c> (docs/standards/stock/functions/LOAD_PATTERN.md): checks a pattern of
    /// the registry for every candle (of the main chart or of another timeframe) in a child Pattern Context. Returns an
    /// Array of Booleans; computed series are stored by timeframe, identifier and completed parameters.
    /// </summary>
    public sealed class LoadPatternFunction : Function
    {
        public FormulaRegistry Registry { get; }

        internal LoadPatternFunction(FormulaRegistry registry)
            : base(StockFunctions.LoadPatternName, CreateOverloads(registry))
        {
            // Computing a series changes nothing for the caller; the stored series are only reused
            SideEffectFree = true;
            Registry = registry;
        }

        private static IFunctionOverload[] CreateOverloads(FormulaRegistry registry)
        {
            ArgumentTemplate id = ArgumentTemplate.Of("id", Types.Text);
            ArgumentTemplate parameters = ArgumentTemplate.Of("parameters", Types.Object);
            ArgumentTemplate options = ArgumentTemplate.Of("options", Types.Object);

            return
            [
                new FunctionOverload([id], (a, c) => Load(registry, c, (string)a[0].Raw!, null, null)),
                new FunctionOverload([id, parameters], (a, c) => Load(registry, c, (string)a[0].Raw!, (IObject)a[1].Raw!, null)),
                new FunctionOverload([id, parameters, options], (a, c) => Load(registry, c, (string)a[0].Raw!, (IObject)a[1].Raw!, (IObject)a[2].Raw!)),
            ];
        }

        private static IValue Load(FormulaRegistry registry, IContext context, string id, IObject? parameters, IObject? options)
        {
            if (context is not StockContext caller)
                return StockFunctions.OperationError($"\"{StockFunctions.LoadPatternName}\" can be called only in a Stock Context.");

            string? timeframe = null;
            bool align = true;

            if (options != null)
            {
                if (StockFunctions.ReadOptions(StockFunctions.LoadPatternName, options, new Dictionary<string, string>
                    {
                        ["timeframe"] = Types.TextName,
                        ["align"] = Types.BooleanName,
                    }, out var read) is IValue optionsError)
                    return optionsError;

                timeframe = read.GetValueOrDefault("timeframe")?.Raw as string;
                align = read.GetValueOrDefault("align")?.Raw is not false;
            }

            StockSeries target = caller.Series;

            if (timeframe != null)
            {
                if (StockFunctions.TimeframeSeries(caller, timeframe, out StockSeries? other) is IValue missing)
                    return missing;

                target = other!;
            }

            // 1-2. Definition and parameters
            if (StockFunctions.PrepareLoad(registry.Patterns, id, parameters, out FormulaDefinition definition, out IValue parametersValue) is IValue error)
                return error;

            string key = StockFunctions.SeriesKey(target, "pattern", $"{id}|{StockFunctions.ParametersKey(parametersValue)}");

            // 3. A pattern that calls itself, directly or indirectly
            if (StockFunctions.CallsItself(caller, key))
                return StockFunctions.OperationError($"Pattern \"{id}\" calls itself.");

            if (!target.TryGetStored(key, out IValue? result))
            {
                // 4-6. Child Pattern Context with its own iterations
                var child = new PatternContext(caller.Environment, target, parametersValue, key, caller);
                child.FixedNow = caller.Now;
                child.PreExecute();

                result = target.Executor.ExecutePatternSeries(definition.Formula, child);
                target.Store(key, result);
            }

            if (StockFunctions.IsError(result!) || timeframe == null)
                return result!;

            return StockFunctions.FromTimeframe(caller, target, result!, align, key);
        }
    }
}
