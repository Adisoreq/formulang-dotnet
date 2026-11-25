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

        /// <summary>
        /// Steps shared by <c>LOAD_INDICATOR</c> and <c>LOAD_PATTERN</c>: finds the definition, completes the parameters
        /// with the default values and checks that the formula does not call itself.
        /// </summary>
        /// <returns><c>null</c> if the formula can be executed, or the Error.</returns>
        internal static IValue? PrepareLoad(string functionName, FormulaCollection part, string id, IObject? parameters,
            IContext context, out StockContext caller, out FormulaDefinition definition, out IValue completedParameters)
        {
            caller = null!;
            definition = null!;
            completedParameters = null!;

            if (context is not StockContext stock)
                return OperationError($"\"{functionName}\" can be called only in a Stock Context.");

            caller = stock;

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
    }

    /// <summary>
    /// The <c>LOAD_INDICATOR</c> function; holds the formula registry of the environment.
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

        private static IFunctionOverload[] CreateOverloads(FormulaRegistry registry)
        {
            ArgumentTemplate id = ArgumentTemplate.Of("id", Types.Text);
            ArgumentTemplate parameters = ArgumentTemplate.Of("parameters", Types.Object);
            ArgumentTemplate source = ArgumentTemplate.Of("source", Types.Array);

            return
            [
                new FunctionOverload([id], (a, c) => Compute(registry, (string)a[0].Raw!, null, null, c)),
                new FunctionOverload([id, parameters], (a, c) => Compute(registry, (string)a[0].Raw!, (IObject)a[1].Raw!, null, c)),
                new FunctionOverload([id, parameters, source], (a, c) => Compute(registry, (string)a[0].Raw!, (IObject)a[1].Raw!, a[2], c)),
            ];
        }

        private static IValue Compute(FormulaRegistry registry, string id, IObject? parameters, IValue? source, IContext context)
        {
            // 1-2. Definition and parameters
            if (StockFunctions.PrepareLoad(StockFunctions.LoadIndicatorName, registry.Indicators, id, parameters, context,
                out StockContext caller, out FormulaDefinition definition, out IValue parametersValue) is IValue error)
                return error;

            StockSeries series = caller.Series;

            // 3. Source
            source ??= series.Close;
            IValue[] elements = (IValue[])source.Raw!;

            if (elements.Length != series.Count)
                return StockFunctions.OperationError($"The source has {elements.Length} elements, but there are {series.Count} candles.");

            if (!series.IsValidSource(elements))
                return StockFunctions.OperationError("Every element of the source must be a Number or null.");

            string key = $"indicator|{id}|{StockFunctions.ParametersKey(parametersValue)}|{series.SourceKey(elements)}";

            // 4. An indicator that calls itself, directly or indirectly
            if (StockFunctions.CallsItself(caller, key))
                return StockFunctions.OperationError($"Indicator \"{id}\" calls itself.");

            if (series.TryGetStored(key, out IValue? stored))
                return stored!;

            // 5-7. Child Indicator Context with its own iterations
            var child = new IndicatorContext(caller.Environment, series, parametersValue, source, key, caller);
            child.FixedNow = caller.Now;
            child.PreExecute();

            IValue result = series.Executor.ExecuteIndicator(definition.Formula, child);
            series.Store(key, result);
            return result;
        }
    }

    /// <summary>
    /// The <c>LOAD_PATTERN</c> function; holds the formula registry of the environment.
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

            return
            [
                new FunctionOverload([id], (a, c) => Compute(registry, (string)a[0].Raw!, null, c)),
                new FunctionOverload([id, parameters], (a, c) => Compute(registry, (string)a[0].Raw!, (IObject)a[1].Raw!, c)),
            ];
        }

        private static IValue Compute(FormulaRegistry registry, string id, IObject? parameters, IContext context)
        {
            // 1-2. Definition and parameters
            if (StockFunctions.PrepareLoad(StockFunctions.LoadPatternName, registry.Patterns, id, parameters, context,
                out StockContext caller, out FormulaDefinition definition, out IValue parametersValue) is IValue error)
                return error;

            StockSeries series = caller.Series;
            string key = $"pattern|{id}|{StockFunctions.ParametersKey(parametersValue)}";

            // 3. A pattern that calls itself, directly or indirectly
            if (StockFunctions.CallsItself(caller, key))
                return StockFunctions.OperationError($"Pattern \"{id}\" calls itself.");

            if (series.TryGetStored(key, out IValue? stored))
                return stored!;

            // 4-6. Child Pattern Context with its own iterations
            var child = new PatternContext(caller.Environment, series, parametersValue, key, caller);
            child.FixedNow = caller.Now;
            child.PreExecute();

            IValue result = series.Executor.ExecutePatternSeries(definition.Formula, child);
            series.Store(key, result);
            return result;
        }
    }
}
