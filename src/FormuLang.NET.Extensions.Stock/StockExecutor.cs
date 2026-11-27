using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Processing;
using FormuLang.NET.Core;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Extensions.Stock.Contexts;

namespace FormuLang.NET.Extensions.Stock
{
    /// <summary>
    /// Executes formulas in the Stock context types and checks their expected results
    /// (docs/standards/stock/README.md, "Context types"). Every method accepts a formula text or a prepared formula
    /// (<see cref="Executor.Prepare"/>).
    /// </summary>
    public static class StockExecutor
    {
        /// <summary>
        /// Executes an indicator formula for every candle (Indicator Context).
        /// </summary>
        /// <returns>
        /// An Array with the value of the indicator for every candle; the Error of an iteration; or
        /// <c>OPERATION_ERROR</c> if an iteration returns a value that is not allowed, or a kind of value other than the
        /// previous non-<c>null</c> results.
        /// </returns>
        public static IValue ExecuteIndicator(this Executor executor, string formula, IndicatorContext context)
            => IndicatorValues(executor.ExecuteIterations(formula, context, stopOnError: true));

        /// <inheritdoc cref="ExecuteIndicator(Executor, string, IndicatorContext)"/>
        public static IValue ExecuteIndicator(this Executor executor, IPreparedFormula formula, IndicatorContext context)
            => IndicatorValues(executor.ExecuteIterations(formula, context, stopOnError: true));

        private static IValue IndicatorValues(IterationExecutionResult run)
        {
            if (!run.Successful)
                return Value.Error(run.Error, "");

            IValue[] values = run.Values.ToArray();
            return IndicatorResultsError(values) ?? Value.Array(values, "");
        }

        /// <summary>
        /// The <c>OPERATION_ERROR</c> of the results of an indicator that break the expected result of the Indicator
        /// Context (every non-null result is a Number, or every one is an Object of Numbers with the same keys), or
        /// <c>null</c>.
        /// </summary>
        public static IValue? IndicatorResultsError(IReadOnlyList<IValue> values)
        {
            string? kind = null;
            HashSet<string>? keys = null;

            for (int i = 0; i < values.Count; i++)
            {
                IValue value = values[i];
                string? current = Kind(value);

                if (current == null)
                    return NotAllowed(i, "a value that is not allowed in the Indicator Context");

                if (current == Types.ObjectName)
                {
                    var objectKeys = ((IObject)value.Raw!).Keys.ToHashSet(StringComparer.Ordinal);

                    if (keys != null && !keys.SetEquals(objectKeys))
                        return NotAllowed(i, "an Object with other keys than the previous results");

                    keys = objectKeys;
                }

                if (current != Types.NullName)
                {
                    if (kind != null && kind != current)
                        return NotAllowed(i, "another kind of value than the previous results");

                    kind = current;
                }
            }

            return null;
        }

        /// <summary>
        /// Kind of an allowed Indicator Context result: Null, Number, or Object with Number values.
        /// </summary>
        private static string? Kind(IValue value)
        {
            switch (value.Type.Base)
            {
                case Types.NullName:
                case Types.NumberName:
                    return value.Type.Base;

                case Types.ObjectName:
                    var obj = (IObject)value.Raw!;
                    return obj.Keys.All(k => obj.GetProperty(k).GetValue().Type.Base == Types.NumberName) ? Types.ObjectName : null;

                default:
                    return null;
            }
        }

        private static IValue NotAllowed(int iteration, string problem)
            => StockFunctions.OperationError($"Iteration {iteration} of the indicator returned {problem}.");

        /// <summary>
        /// The <c>OPERATION_ERROR</c> of an iteration of a pattern that returned a value that is not a Boolean.
        /// </summary>
        public static IError PatternResultError(int iteration)
            => Errors.OperationError($"Iteration {iteration} of the pattern returned a value that is not a Boolean.");

        /// <summary>
        /// The <c>OPERATION_ERROR</c> of an iteration of a strategy that returned neither a Number nor a signal object.
        /// </summary>
        public static IError StrategyResultError(int iteration)
            => Errors.OperationError($"Iteration {iteration} of the strategy returned a value that is neither a Number nor a signal object.");

        /// <summary>
        /// Executes a pattern formula for every candle (Pattern Context) and collects the candles for which it returned
        /// <c>true</c>.
        /// </summary>
        /// <param name="stopOnError">Stops at the first Error, or at the first result that is not a Boolean.</param>
        public static PatternResult ExecutePattern(this Executor executor, string formula, PatternContext context, bool stopOnError = true)
            => PatternMatches(executor.ExecuteIterations(formula, context, stopOnError), stopOnError);

        /// <inheritdoc cref="ExecutePattern(Executor, string, PatternContext, bool)"/>
        public static PatternResult ExecutePattern(this Executor executor, IPreparedFormula formula, PatternContext context, bool stopOnError = true)
            => PatternMatches(executor.ExecuteIterations(formula, context, stopOnError), stopOnError);

        private static PatternResult PatternMatches(IterationExecutionResult run, bool stopOnError)
        {
            var indices = new List<int>();
            IError error = run.Error;

            for (int i = 0; i < run.Results.Count; i++)
            {
                IValue value = run.Results[i].Value;

                if (value.Type.Base == Types.BooleanName)
                {
                    if (value.Raw is true)
                        indices.Add(i);
                }
                else if (run.Results[i].Successful && error.IsSuccess())
                {
                    error = PatternResultError(i);
                    if (stopOnError)
                        break;
                }
            }

            return new PatternResult(indices, error, run);
        }

        /// <summary>
        /// Executes a pattern formula for every candle and returns its whole series, as <c>LOAD_PATTERN</c> does
        /// (docs/standards/stock/functions/LOAD_PATTERN.md).
        /// </summary>
        /// <returns>
        /// An Array of <c>N</c> Booleans; the Error of an iteration; or <c>OPERATION_ERROR</c> if an iteration returns a
        /// value that is not a Boolean.
        /// </returns>
        public static IValue ExecutePatternSeries(this Executor executor, string formula, PatternContext context)
            => PatternSeries(executor.ExecutePattern(formula, context, stopOnError: true));

        /// <inheritdoc cref="ExecutePatternSeries(Executor, string, PatternContext)"/>
        public static IValue ExecutePatternSeries(this Executor executor, IPreparedFormula formula, PatternContext context)
            => PatternSeries(executor.ExecutePattern(formula, context, stopOnError: true));

        private static IValue PatternSeries(PatternResult result)
            => result.Successful ? Value.Array(result.Iterations.Values, "") : Value.Error(result.Error, "");

        /// <summary>
        /// Executes a strategy formula for every candle (Strategy Context). Every result must be a Number, or since
        /// <c>stock-1.2</c> a signal object <c>{ signal, reason?, checks?, ... }</c>; signals outside of the range from
        /// <c>SELL</c> to <c>BUY</c> are clamped. <c>PARAMETERS</c> is first completed with the <c>default</c> values of
        /// the metadata of the formula (<see cref="StrategyContext.CompleteParameters"/>).
        /// </summary>
        /// <param name="stopOnError">
        /// Stops at the first Error, or at the first result that is neither a Number nor a signal object.
        /// </param>
        public static StrategyResult ExecuteStrategy(this Executor executor, string formula, StrategyContext context, bool stopOnError = true)
            => executor.ExecuteStrategy(executor.Prepare(formula, context), context, stopOnError);

        /// <inheritdoc cref="ExecuteStrategy(Executor, string, StrategyContext, bool)"/>
        public static StrategyResult ExecuteStrategy(this Executor executor, IPreparedFormula formula, StrategyContext context, bool stopOnError = true)
        {
            IterationExecutionResult run;

            if (formula is PreparedFormula { Failure: ExecutionResult failure })
            {
                run = new IterationExecutionResult([failure]);
            }
            else
            {
                context.CompleteParameters(formula.Metadata);
                run = executor.ExecuteIterations(formula, context, stopOnError);
            }

            var signals = new List<double?>(run.Results.Count);
            var signalObjects = new List<IObject?>(run.Results.Count);
            IError error = run.Error;

            for (int i = 0; i < run.Results.Count; i++)
            {
                IValue value = run.Results[i].Value;
                IValue signal = value;
                IObject? obj = null;

                if (value.Type.Base == Types.ObjectName && value.Raw is IObject signalObject)
                {
                    // A valid signal object; an invalid one is an Error of the iteration (StrategyContext.FinalizeResult)
                    obj = signalObject;
                    signal = obj.GetProperty("signal").GetValue();
                }

                if (signal.Raw is double number && signal.Type.Base == Types.NumberName)
                {
                    signals.Add(Math.Clamp(number, StrategyContext.Sell, StrategyContext.Buy));
                    signalObjects.Add(obj);
                    continue;
                }

                signals.Add(null);
                signalObjects.Add(null);

                if (run.Results[i].Successful && error.IsSuccess())
                {
                    error = StrategyResultError(i);
                    if (stopOnError)
                        break;
                }
            }

            return new StrategyResult(signals, error, run) { SignalObjects = signalObjects };
        }
    }

    /// <param name="Indices">0-based indices of the candles at which the pattern starts.</param>
    /// <param name="Error">The first Error, or <c>SUCCESS</c>.</param>
    public sealed record PatternResult(IReadOnlyList<int> Indices, IError Error, IterationExecutionResult Iterations)
    {
        public bool Successful => Error.IsSuccess();
    }

    /// <param name="Signals">Signal of every executed iteration, from -1 to 1; <c>null</c> for a failed iteration.</param>
    /// <param name="Error">The first Error, or <c>SUCCESS</c>.</param>
    public sealed record StrategyResult(IReadOnlyList<double?> Signals, IError Error, IterationExecutionResult Iterations)
    {
        public bool Successful => Error.IsSuccess();

        /// <summary>
        /// The signal object of every executed iteration (since <c>stock-1.2</c>, with <c>signal</c>, <c>reason</c>,
        /// <c>checks</c> and the keys of the host application), or <c>null</c> where the formula returned a Number or no
        /// signal was computed.
        /// </summary>
        public IReadOnlyList<IObject?> SignalObjects { get; init; } = [];
    }
}
