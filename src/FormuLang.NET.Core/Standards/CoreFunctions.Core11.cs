using FormuLang.NET.Abstractions.Functions;
using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Functions;
using FormuLang.NET.Core.Primitives;
using static FormuLang.NET.Core.Standards.FunctionHelpers;

namespace FormuLang.NET.Core.Standards
{
    public static partial class CoreFunctions
    {
        /// <summary>
        /// Functions added in <c>core-1.1</c>: <c>CALL</c>, <c>FIRST</c>, <c>LAST</c>, <c>IFNULL</c>, <c>IFERR</c>,
        /// <c>ISINT</c>, <c>ISEVEN</c>, <c>ISODD</c>, the statistical functions and <c>MERGE</c>.
        /// </summary>
        private static IEnumerable<Function> Core11Functions() =>
        [
            // Control flow
            Fn("CALL", Overload((a, c) => CallDelegate(Del(a[0]), a.Skip(1).ToArray(), c),
                DelegateArg("function"), ArgumentTemplate.Any("arguments", optional: true, multiple: true))),
            Fn("IFNULL", Overload((a, _) => a[0].Type.Base == Types.NullName ? a[1] : a[0], AnyArg("value"), AnyArg("fallback"))),
            Fn("IFERR", Overload((a, _) => IsError(a[0]) ? a[1] : a[0], AnyArg("value"), AnyArg("fallback"))),

            // Arrays
            Fn("FIRST",
                Overload((a, _) => Arr(a[0]).Length == 0 ? Value.Null("null") : Arr(a[0])[0], ArrayArg("array")),
                Overload((a, c) => FindElement("FIRST", Arr(a[0]), Del(a[1]), fromEnd: false, c), ArrayArg("array"), DelegateArg("handler"))),
            Fn("LAST",
                Overload((a, _) => Arr(a[0]).Length == 0 ? Value.Null("null") : Arr(a[0])[^1], ArrayArg("array")),
                Overload((a, c) => FindElement("LAST", Arr(a[0]), Del(a[1]), fromEnd: true, c), ArrayArg("array"), DelegateArg("handler"))),

            // Types
            Fn("ISINT", Overload((a, _) => B(a[0].Raw is double d && IsWhole(d)), AnyArg("value"))),
            Fn("ISEVEN", Overload((a, _) => B(a[0].Raw is double d && IsWhole(d) && Math.IEEERemainder(d, 2) == 0), AnyArg("value"))),
            Fn("ISODD", Overload((a, _) => B(a[0].Raw is double d && IsWhole(d) && Math.IEEERemainder(d, 2) != 0), AnyArg("value"))),

            // Statistics
            Fn("MEDIAN", Overload((a, _) => Median(Arr(a[0]).Select(Num).ToArray()), NumberArrayArg("numbers"))),
            Fn("VAR_P", Overload((a, _) => Variance(Arr(a[0]), sample: false, root: false), NumberArrayArg("numbers"))),
            Fn("VAR_S", Overload((a, _) => Variance(Arr(a[0]), sample: true, root: false), NumberArrayArg("numbers"))),
            Fn("STDEV_P", Overload((a, _) => Variance(Arr(a[0]), sample: false, root: true), NumberArrayArg("numbers"))),
            Fn("STDEV_S", Overload((a, _) => Variance(Arr(a[0]), sample: true, root: true), NumberArrayArg("numbers"))),

            // Objects
            Fn("MERGE",
                Overload((a, _) => Merge(Obj(a[0]), Obj(a[1])), ObjectArg("object1"), ObjectArg("object2")),
                // core-1.3: more than two objects, merged from the left
                Overload((a, _) => a.Skip(1).Aggregate(a[0], (merged, next) => Merge(Obj(merged), Obj(next))),
                    ObjectArg("object1"), ObjectArg("object2"), ArgumentTemplate.Of("objects", Types.Object, multiple: true))),
        ];

        /// <summary>
        /// <c>CALL(function, arguments...)</c>: a lambda is invoked, a named function is called like a call written with
        /// its name (overload matching), a procedure cannot be called through a delegate.
        /// </summary>
        private static IValue CallDelegate(IFunction function, IValue[] arguments, IContext context)
        {
            if (function.FunctionType == FunctionType.Procedure)
                return OperationError($"Procedure \"{function.Name}\" cannot be called through a delegate.");

            return function.Invoke(arguments, context);
        }

        private static IValue FindElement(string name, IValue[] array, IFunction handler, bool fromEnd, IContext context)
        {
            if (CheckHandler(handler, name) is IValue error)
                return error;

            for (int k = 0; k < array.Length; k++)
            {
                IValue element = array[fromEnd ? array.Length - 1 - k : k];
                IValue result = Call(handler, element, context);

                if (IsError(result))
                    return result;

                if (result.Raw is not bool found || result.Type.Base != Types.BooleanName)
                    return OperationError($"The handler passed to \"{name}\" must return a Boolean.");

                if (found)
                    return element;
            }

            return Value.Null("null");
        }

        private static IValue Median(double[] numbers)
        {
            if (numbers.Length == 0)
                return OperationError("The array is empty.");

            Array.Sort(numbers);
            int middle = numbers.Length / 2;
            return N(numbers.Length % 2 == 1 ? numbers[middle] : (numbers[middle - 1] + numbers[middle]) / 2);
        }

        private static IValue Variance(IValue[] values, bool sample, bool root)
        {
            int count = values.Length;

            if (sample && count < 2)
                return OperationError("The sample variance needs at least 2 numbers.");

            if (count == 0)
                return OperationError("The array is empty.");

            double mean = values.Average(Num);
            double sum = values.Sum(v => (Num(v) - mean) * (Num(v) - mean));
            double variance = sum / (sample ? count - 1 : count);
            return N(root ? Math.Sqrt(variance) : variance);
        }

        private static IValue Merge(IObject first, IObject second)
        {
            var merged = new Primitives.Object();

            foreach (string key in first.Keys)
                merged.InitVariable(key, (second.HasKey(key) ? second : first).GetProperty(key).GetValue());

            foreach (string key in second.Keys)
                merged.InitVariable(key, second.GetProperty(key).GetValue());   // keys of the first object are not replaced

            return Value.Object(merged, "");
        }
    }
}
