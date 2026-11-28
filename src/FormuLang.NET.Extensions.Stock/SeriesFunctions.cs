using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core;
using FormuLang.NET.Core.Functions;
using FormuLang.NET.Core.Primitives;

namespace FormuLang.NET.Extensions.Stock
{
    /// <summary>
    /// Series functions (docs/standards/stock/functions/README.md, "Series functions") and the strategy function
    /// <c>SCORE</c> of <c>stock-1.2</c>, and <c>SIGNAL_SCORE</c> of <c>stock-1.3</c>.
    /// </summary>
    /// <remarks>
    /// Common rules of the series functions: they can be called only in a Stock Context; a series is an Array of <c>N</c>
    /// elements; they work at the current candle (positions do not depend on <c>@array_indexing</c>, and the elements
    /// after the current candle are never read); missing data (a <c>null</c>, or not enough candles) gives <c>false</c>
    /// or <c>null</c>, never an Error.
    /// </remarks>
    public static class SeriesFunctions
    {
        public const string ReadyName = "READY";
        public const string CrossoverName = "CROSSOVER";
        public const string CrossunderName = "CROSSUNDER";
        public const string HighestName = "HIGHEST";
        public const string LowestName = "LOWEST";
        public const string RisingName = "RISING";
        public const string FallingName = "FALLING";
        public const string BarsSinceName = "BARS_SINCE";
        public const string ValueWhenName = "VALUE_WHEN";
        public const string ScoreName = "SCORE";
        public const string SignalScoreName = "SIGNAL_SCORE";

        private static ArgumentTemplate SeriesArg(string name) => ArgumentTemplate.Of(name, Types.Array);
        private static ArgumentTemplate NumberArg(string name) => ArgumentTemplate.Of(name, Types.Number);
        private static IValue[] Elements(IValue value) => (IValue[])value.Raw!;
        private static double Numeric(IValue value) => (double)value.Raw!;

        private static Function SeriesFunction(string name, params FunctionOverload[] overloads)
            => new(name, overloads) { SideEffectFree = true };

        private static IValue Boolean(bool value) => Value.Boolean(value, value ? "true" : "false");
        private static IValue Null() => Value.Null("null");
        private static IValue OperationError(string message) => Value.Error(Errors.OperationError(message), "");
        private static IValue ArgumentError(string message) => Value.Error(Errors.ArgumentError(message), "");
        private static IValue InvalidKeyError(string message) => Value.Error(Errors.InvalidKeyError(message), "");

        private static bool IsWhole(double value) => double.IsFinite(value) && Math.Floor(value) == value;
        private static bool IsNull(IValue value) => value.Type.Base == Types.NullName;
        private static bool IsNumber(IValue value) => value.Type.Base == Types.NumberName;

        /// <summary>A <c>length</c> or <c>n</c>: a whole number of 1 or more; otherwise the <c>OPERATION_ERROR</c>.</summary>
        private static IValue? LengthError(string name, double value, string what = "length")
            => IsWhole(value) && value >= 1 ? null : OperationError($"The {what} of \"{name}\" must be a whole number of 1 or more.");

        /// <summary>An <c>offset</c> or <c>occurrence</c>: a whole number of 0 or more; otherwise the <c>OPERATION_ERROR</c>.</summary>
        private static IValue? OffsetError(string name, double value, string what)
            => IsWhole(value) && value >= 0 ? null : OperationError($"The {what} of \"{name}\" must be a whole number of 0 or more.");

        private static IValue NotNumber(string name) => OperationError($"A value of the series of \"{name}\" is neither a Number nor null.");
        private static IValue NotBoolean(string name) => OperationError($"A value of the condition of \"{name}\" is neither a Boolean nor null.");

        /// <summary>
        /// The series functions of <c>stock-1.2</c>: <c>READY</c>, <c>CROSSOVER</c>, <c>CROSSUNDER</c>, <c>HIGHEST</c>,
        /// <c>LOWEST</c>, <c>RISING</c>, <c>FALLING</c>, <c>BARS_SINCE</c> and <c>VALUE_WHEN</c>.
        /// </summary>
        public static IReadOnlyList<Function> All { get; } =
        [
            SeriesFunction(ReadyName,
                new FunctionOverload([SeriesArg("series"), NumberArg("n")], (a, c) => Ready(Elements(a[0]), Numeric(a[1]), c))),
            CrossFunction(CrossoverName, above: true),
            CrossFunction(CrossunderName, above: false),
            ExtremeFunction(HighestName, highest: true),
            ExtremeFunction(LowestName, highest: false),
            MonotonicFunction(RisingName, rising: true),
            MonotonicFunction(FallingName, rising: false),
            SeriesFunction(BarsSinceName,
                new FunctionOverload([SeriesArg("condition")], (a, c) => BarsSince(Elements(a[0]), c))),
            SeriesFunction(ValueWhenName,
                new FunctionOverload([SeriesArg("condition"), SeriesArg("series")], (a, c) => ValueWhen(Elements(a[0]), Elements(a[1]), 0, c)),
                new FunctionOverload([SeriesArg("condition"), SeriesArg("series"), NumberArg("occurrence")],
                    (a, c) => ValueWhen(Elements(a[0]), Elements(a[1]), Numeric(a[2]), c))),
        ];

        // READY: docs/standards/stock/functions/READY.md

        private static IValue Ready(IValue[] series, double n, IContext context)
        {
            if (StockFunctions.CurrentPosition(ReadyName, series, context, out int position) is IValue error)
                return error;

            if (LengthError(ReadyName, n, "n") is IValue lengthError)
                return lengthError;

            if (position + 1 < n)
                return Boolean(false);

            for (int i = position - (int)n + 1; i <= position; i++)
            {
                if (IsNull(series[i]))
                    return Boolean(false);
            }

            return Boolean(true);
        }

        // CROSSOVER and CROSSUNDER: a series, or a Number that is the same at every candle

        private static Function CrossFunction(string name, bool above)
        {
            IValue Implementation(IReadOnlyList<IValue> a, IContext c) => Cross(name, a[0], a[1], above, c);

            return SeriesFunction(name,
                new FunctionOverload([SeriesArg("a"), SeriesArg("b")], Implementation),
                new FunctionOverload([SeriesArg("a"), NumberArg("b")], Implementation),
                new FunctionOverload([NumberArg("a"), SeriesArg("b")], Implementation));
        }

        private static IValue Cross(string name, IValue a, IValue b, bool above, IContext context)
        {
            int? position = null;

            foreach (IValue operand in new[] { a, b })
            {
                if (operand.Type.Base != Types.ArrayName)
                    continue;

                if (StockFunctions.CurrentPosition(name, Elements(operand), context, out int current) is IValue error)
                    return error;

                position = current;
            }

            if (position is not int at)
                return OperationError($"\"{name}\" needs a series.");

            if (at == 0)
                return Boolean(false);

            static IValue At(IValue operand, int i) => operand.Type.Base == Types.ArrayName ? ((IValue[])operand.Raw!)[i] : operand;

            IValue[] values = [At(a, at - 1), At(b, at - 1), At(a, at), At(b, at)];

            if (values.Any(IsNull))
                return Boolean(false);

            if (!values.All(IsNumber))
                return NotNumber(name);

            double a0 = Numeric(values[0]), b0 = Numeric(values[1]), a1 = Numeric(values[2]), b1 = Numeric(values[3]);
            return Boolean(above ? a0 <= b0 && a1 > b1 : a0 >= b0 && a1 < b1);
        }

        // HIGHEST and LOWEST

        private static Function ExtremeFunction(string name, bool highest) => SeriesFunction(name,
            new FunctionOverload([SeriesArg("series"), NumberArg("length")], (a, c) => Extreme(name, Elements(a[0]), Numeric(a[1]), 0, highest, c)),
            new FunctionOverload([SeriesArg("series"), NumberArg("length"), NumberArg("offset")],
                (a, c) => Extreme(name, Elements(a[0]), Numeric(a[1]), Numeric(a[2]), highest, c)));

        private static IValue Extreme(string name, IValue[] series, double length, double offset, bool highest, IContext context)
        {
            if (StockFunctions.CurrentPosition(name, series, context, out int position) is IValue error)
                return error;

            if ((LengthError(name, length) ?? OffsetError(name, offset, "offset")) is IValue argumentError)
                return argumentError;

            int end = position - (int)offset;

            if (end + 1 < length)
                return Null();

            double? result = null;

            for (int i = end - (int)length + 1; i <= end; i++)
            {
                IValue value = series[i];

                if (IsNull(value))
                    continue;

                if (!IsNumber(value))
                    return NotNumber(name);

                double number = Numeric(value);

                if (result == null || (highest ? number > result : number < result))
                    result = number;
            }

            return result is double found ? Value.Number(found) : Null();
        }

        // RISING and FALLING

        private static Function MonotonicFunction(string name, bool rising) => SeriesFunction(name,
            new FunctionOverload([SeriesArg("series"), NumberArg("length")], (a, c) => Monotonic(name, Elements(a[0]), Numeric(a[1]), rising, c)));

        private static IValue Monotonic(string name, IValue[] series, double length, bool rising, IContext context)
        {
            if (StockFunctions.CurrentPosition(name, series, context, out int position) is IValue error)
                return error;

            if (LengthError(name, length) is IValue lengthError)
                return lengthError;

            if (position < length)
                return Boolean(false);

            IValue[] window = series[(position - (int)length)..(position + 1)];

            if (window.Any(IsNull))
                return Boolean(false);

            if (!window.All(IsNumber))
                return NotNumber(name);

            for (int i = 1; i < window.Length; i++)
            {
                double previous = Numeric(window[i - 1]);
                double current = Numeric(window[i]);

                if (rising ? !(current > previous) : !(current < previous))
                    return Boolean(false);
            }

            return Boolean(true);
        }

        // BARS_SINCE and VALUE_WHEN

        /// <summary>
        /// The position of the <c>true</c> element number <c>occurrence + 1</c> of the condition, counted from the current
        /// candle back to the first one (<c>-1</c> if there is none), or the <c>OPERATION_ERROR</c> of an element that is
        /// neither a Boolean nor null.
        /// </summary>
        private static IValue? FindTrue(string name, IValue[] condition, int position, int occurrence, out int found)
        {
            found = -1;
            int count = 0;

            for (int i = position; i >= 0; i--)
            {
                IValue value = condition[i];

                if (value.Type.Base == Types.BooleanName)
                {
                    if (value.Raw is true && count++ == occurrence)
                    {
                        found = i;
                        return null;
                    }
                }
                else if (!IsNull(value))
                {
                    return NotBoolean(name);
                }
            }

            return null;
        }

        private static IValue BarsSince(IValue[] condition, IContext context)
        {
            if (StockFunctions.CurrentPosition(BarsSinceName, condition, context, out int position) is IValue error)
                return error;

            if (FindTrue(BarsSinceName, condition, position, 0, out int found) is IValue notBoolean)
                return notBoolean;

            return found < 0 ? Null() : Value.Number(position - found);
        }

        private static IValue ValueWhen(IValue[] condition, IValue[] series, double occurrence, IContext context)
        {
            if (StockFunctions.CurrentPosition(ValueWhenName, condition, context, out int position) is IValue error)
                return error;

            if (StockFunctions.CurrentPosition(ValueWhenName, series, context, out _) is IValue seriesError)
                return seriesError;

            if (OffsetError(ValueWhenName, occurrence, "occurrence") is IValue occurrenceError)
                return occurrenceError;

            if (FindTrue(ValueWhenName, condition, position, (int)occurrence, out int found) is IValue notBoolean)
                return notBoolean;

            return found < 0 ? Null() : series[found];
        }

        // SCORE (strategy function; it does not need a Stock Context): docs/standards/stock/functions/SCORE.md

        /// <summary>
        /// <c>SCORE(conditions, weights?)</c>: the share of <c>true</c> values of an Object or an Array of Booleans,
        /// optionally weighted. Since <c>stock-1.3</c> a <c>null</c> condition is a condition that is not met.
        /// </summary>
        public static Function Score { get; } = SeriesFunction(ScoreName,
            new FunctionOverload([ArgumentTemplate.Of("conditions", Types.Object)], (a, _) => ScoreObject((IObject)a[0].Raw!, null)),
            new FunctionOverload([Conditions("conditions")], (a, _) => ScoreArray(Elements(a[0]), null)),
            new FunctionOverload([ArgumentTemplate.Of("conditions", Types.Object), ArgumentTemplate.Of("weights", Types.Object)],
                (a, _) => ScoreObject((IObject)a[0].Raw!, (IObject)a[1].Raw!)),
            new FunctionOverload([Conditions("conditions"), ArgumentTemplate.ArrayOf("weights", Types.Number)],
                (a, _) => ScoreArray(Elements(a[0]), Elements(a[1]))));

        /// <summary>
        /// An <c>Array&lt;Boolean&gt;</c> of conditions whose elements may also be <c>null</c> (<c>stock-1.3</c>).
        /// </summary>
        private static ArgumentTemplate Conditions(string name) => ArgumentTemplate.ArrayOf(name, Types.Boolean, nullable: true);

        private static IValue Share(double met, double total) => Value.Number(total == 0 ? 0 : met / total);

        private static IValue ScoreObject(IObject conditions, IObject? weights, string name = ScoreName)
        {
            var values = new List<(string Key, bool Value)>();

            foreach (string key in conditions.Keys)
            {
                IValue value = conditions.GetProperty(key).GetValue();

                if (value.Type.Base == Types.ErrorName)
                    return value;

                // stock-1.3: a null condition is a condition that is not met
                if (IsNull(value))
                {
                    values.Add((key, false));
                    continue;
                }

                if (value.Type.Base != Types.BooleanName)
                    return ArgumentError($"The condition \"{key}\" of \"{name}\" is not a Boolean.");

                values.Add((key, (bool)value.Raw!));
            }

            var weightOf = new Dictionary<string, double>(StringComparer.Ordinal);
            var weightOrder = new List<string>();

            foreach (string key in weights?.Keys ?? [])
            {
                IValue weight = weights!.GetProperty(key).GetValue();

                if (weight.Type.Base == Types.ErrorName)
                    return weight;

                if (weight.Type.Base != Types.NumberName)
                    return ArgumentError($"The weight \"{key}\" of \"{name}\" is not a Number.");

                weightOf[key] = Numeric(weight);
                weightOrder.Add(key);
            }

            foreach (string key in weightOrder)
            {
                if (!values.Any(v => v.Key == key))
                    return InvalidKeyError($"The weight \"{key}\" of \"{name}\" is not a key of the conditions.");
            }

            foreach (string key in weightOrder)
            {
                if (weightOf[key] < 0)
                    return OperationError($"The weight \"{key}\" of \"{name}\" is negative.");
            }

            double met = 0, total = 0;

            foreach (var (key, value) in values)
            {
                double weight = weightOf.GetValueOrDefault(key, 1);
                total += weight;

                if (value)
                    met += weight;
            }

            return Share(met, total);
        }

        private static IValue ScoreArray(IValue[] conditions, IValue[]? weights, string name = ScoreName)
        {
            if (weights != null && weights.Length != conditions.Length)
                return OperationError($"The conditions and the weights of \"{name}\" have different lengths.");

            double met = 0, total = 0;

            for (int i = 0; i < conditions.Length; i++)
            {
                double weight = weights == null ? 1 : Numeric(weights[i]);

                if (weight < 0)
                    return OperationError($"A weight of \"{name}\" is negative.");

                total += weight;

                if (conditions[i].Raw is true)
                    met += weight;
            }

            return Share(met, total);
        }

        // SIGNAL_SCORE (stock-1.3; it does not need a Stock Context): docs/standards/stock/functions/SIGNAL_SCORE.md

        /// <summary>
        /// <c>SIGNAL_SCORE(buy, sell, weights?)</c>: <c>SCORE(buy) - SCORE(sell)</c>, the strength of a signal from
        /// <c>-1</c> to <c>1</c>. <c>buy</c> and <c>sell</c> are Objects or Arrays of Booleans (or <c>null</c>), in any
        /// combination; the optional <c>weights</c> (only with Objects) is one Object for the keys of both sides.
        /// </summary>
        public static Function SignalScore { get; } = CreateSignalScore();

        private static Function CreateSignalScore()
        {
            Func<string, ArgumentTemplate>[] sides = [name => ArgumentTemplate.Of(name, Types.Object), Conditions];

            FunctionOverload[] overloads =
            [
                .. sides.SelectMany(buy => sides.Select(sell =>
                    new FunctionOverload([buy("buy"), sell("sell")], (a, _) => SignalScoreOf(a[0], a[1], null)))),
                new FunctionOverload(
                    [ArgumentTemplate.Of("buy", Types.Object), ArgumentTemplate.Of("sell", Types.Object), ArgumentTemplate.Of("weights", Types.Object)],
                    (a, _) => SignalScoreOf(a[0], a[1], (IObject)a[2].Raw!)),
            ];

            return SeriesFunction(SignalScoreName, overloads);
        }

        /// <summary>
        /// First the keys of <paramref name="weights"/> that are keys of neither side (<c>INVALID_KEY_ERROR</c>), then
        /// <c>SCORE</c> of the buy side and of the sell side, each with the weights of its own keys (a key of both sides
        /// gets its weight on both).
        /// </summary>
        private static IValue SignalScoreOf(IValue buy, IValue sell, IObject? weights)
        {
            if (weights != null)
            {
                IObject buyConditions = (IObject)buy.Raw!, sellConditions = (IObject)sell.Raw!;

                foreach (string key in weights.Keys)
                {
                    if (!buyConditions.HasKey(key) && !sellConditions.HasKey(key))
                        return InvalidKeyError($"The weight \"{key}\" of \"{SignalScoreName}\" is not a key of the buy or the sell conditions.");
                }
            }

            IValue buyScore = SideScore(buy, weights);
            if (buyScore.Type.Base == Types.ErrorName)
                return buyScore;

            IValue sellScore = SideScore(sell, weights);
            if (sellScore.Type.Base == Types.ErrorName)
                return sellScore;

            return Value.Number(Numeric(buyScore) - Numeric(sellScore));
        }

        private static IValue SideScore(IValue side, IObject? weights)
        {
            if (side.Type.Base == Types.ArrayName)
                return ScoreArray(Elements(side), null, SignalScoreName);

            var conditions = (IObject)side.Raw!;
            return ScoreObject(conditions, weights == null ? null : WeightsFor(conditions, weights), SignalScoreName);
        }

        /// <summary>
        /// The weights of the keys of <paramref name="weights"/> that are keys of <paramref name="conditions"/>.
        /// </summary>
        private static IObject WeightsFor(IObject conditions, IObject weights)
        {
            var result = new Core.Primitives.Object();

            foreach (string key in weights.Keys.Where(conditions.HasKey))
                result.InitVariable(key, weights.GetProperty(key).GetValue());

            return result;
        }
    }
}
