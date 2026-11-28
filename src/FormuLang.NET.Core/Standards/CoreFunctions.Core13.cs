using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Functions;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Utils;
using FormuLang.NET.Core.Validation;
using static FormuLang.NET.Core.Standards.FunctionHelpers;

namespace FormuLang.NET.Core.Standards
{
    public static partial class CoreFunctions
    {
        /// <summary>
        /// Functions added in <c>core-1.3</c>: the array functions <c>APPEND</c>, <c>INSERT</c>, <c>PUT</c>,
        /// <c>REMOVE</c> and the object functions <c>VALUES</c>, <c>ENTRIES</c>, <c>FROM_ENTRIES</c>, <c>PUT</c>,
        /// <c>REMOVE</c>. They return a new array or object and never change their arguments. The other
        /// <c>core-1.3</c> changes (missing values, <c>GET</c> with a fallback, <c>CONCAT</c> of arrays, <c>MERGE</c> of
        /// many objects) are overloads of the existing functions.
        /// </summary>
        private static IEnumerable<Function> Core13Functions() =>
        [
            // Arrays
            Fn("APPEND", Overload((a, _) => A([.. Arr(a[0]), a[1]]), ArrayArg("array"), AnyArg("value"))),
            Fn("INSERT", Overload((a, c) => Insert(Arr(a[0]), Num(a[1]), a[2], c), ArrayArg("array"), NumberArg("index"), AnyArg("value"))),
            Fn("PUT",
                Overload((a, c) => PutElement(Arr(a[0]), Num(a[1]), a[2], c), ArrayArg("array"), NumberArg("index"), AnyArg("value")),
                Overload((a, _) => PutKey(Obj(a[0]), Txt(a[1]), a[2]), ObjectArg("object"), TextArg("key"), AnyArg("value"))),
            Fn("REMOVE",
                Overload((a, c) => RemoveElement(Arr(a[0]), Num(a[1]), c), ArrayArg("array"), NumberArg("index")),
                Overload((a, _) => RemoveKey(Obj(a[0]), Txt(a[1])), ObjectArg("object"), TextArg("key"))),

            // Objects
            Fn("VALUES", Overload((a, _) => A(EntriesOf(Obj(a[0])).Select(e => e.Value)), ObjectArg("object"))),
            Fn("ENTRIES", Overload((a, _) => A(EntriesOf(Obj(a[0])).Select(e => A([T(e.Key), e.Value]))), ObjectArg("object"))),
            Fn("FROM_ENTRIES", Overload((a, _) => FromEntries(Arr(a[0])), ArrayArg("entries"))),
        ];

        private static IValue NotIdentifier(string key) => InvalidKeyError($"Key \"{key}\" is not a valid identifier.");

        /// <summary>
        /// <c>INSERT(array, index, value)</c>: docs/standards/core/functions/INSERT.md. The index may be the length.
        /// </summary>
        private static IValue Insert(IValue[] array, double index, IValue value, IContext context)
        {
            double at = index - ArrayIndexing(context);

            if (!IsWhole(at) || at < 0 || at > array.Length)
                return InvalidIndexError($"Index {StringConverter.NumberToString(index)} is not a valid index of the array.");

            int position = (int)at;
            return A([.. array[..position], value, .. array[position..]]);
        }

        /// <summary>
        /// <c>PUT(array, index, value)</c>: docs/standards/core/functions/PUT.md
        /// </summary>
        private static IValue PutElement(IValue[] array, double index, IValue value, IContext context)
        {
            if (!TryResolveIndex(index, array.Length, context, out int at, out IValue? error))
                return error!;

            var copy = (IValue[])array.Clone();
            copy[at] = value;
            return A(copy);
        }

        /// <summary>
        /// <c>REMOVE(array, index)</c>: docs/standards/core/functions/REMOVE.md
        /// </summary>
        private static IValue RemoveElement(IValue[] array, double index, IContext context)
        {
            if (!TryResolveIndex(index, array.Length, context, out int at, out IValue? error))
                return error!;

            return A([.. array[..at], .. array[(at + 1)..]]);
        }

        private static IEnumerable<(string Key, IValue Value)> EntriesOf(IObject obj)
            => obj.Keys.Select(key => (key, obj.GetProperty(key).GetValue()));

        /// <summary>
        /// A new object (without an extended type) with the entries, in their order.
        /// </summary>
        private static IValue ObjectOf(IEnumerable<(string Key, IValue Value)> entries)
        {
            var result = new Primitives.Object();

            foreach (var (key, value) in entries)
                result.InitVariable(key, value);

            return Value.Object(result, "");
        }

        /// <summary>
        /// <c>PUT(object, key, value)</c>: <c>MERGE(object, { key: value })</c>; a replaced key keeps its position.
        /// </summary>
        private static IValue PutKey(IObject obj, string key, IValue value)
        {
            if (!IdentifierValidator.Validate(key))
                return NotIdentifier(key);

            var entries = EntriesOf(obj).Select(e => e.Key == key ? (e.Key, value) : e).ToList();

            if (!obj.HasKey(key))
                entries.Add((key, value));

            return ObjectOf(entries);
        }

        /// <summary>
        /// <c>REMOVE(object, key)</c>: docs/standards/core/functions/REMOVE.md
        /// </summary>
        private static IValue RemoveKey(IObject obj, string key)
        {
            if (!obj.HasKey(key))
                return InvalidKeyError($"Key \"{key}\" does not exist.");

            return ObjectOf(EntriesOf(obj).Where(e => e.Key != key));
        }

        /// <summary>
        /// <c>FROM_ENTRIES(entries)</c>: docs/standards/core/functions/FROM_ENTRIES.md. Every element is checked first
        /// (an Error element is returned, an element that is not a pair with a Text key is an <c>ARGUMENT_ERROR</c>),
        /// then the keys (<c>INVALID_KEY_ERROR</c>). A repeated key keeps its first position and gets the value of its
        /// last pair.
        /// </summary>
        private static IValue FromEntries(IValue[] entries)
        {
            foreach (IValue entry in entries)
            {
                if (IsError(entry))
                    return entry;

                if (entry.Raw is not IValue[] pair || entry.Type.Base != Types.ArrayName
                    || pair.Length != 2 || pair[0].Type.Base != Types.TextName)
                    return ArgumentError("An element of \"FROM_ENTRIES\" is not a pair [key, value] with a Text key.");
            }

            var order = new List<string>();
            var values = new Dictionary<string, IValue>(StringComparer.Ordinal);

            foreach (IValue entry in entries)
            {
                IValue[] pair = Arr(entry);
                string key = Txt(pair[0]);

                if (!IdentifierValidator.Validate(key))
                    return NotIdentifier(key);

                if (!values.ContainsKey(key))
                    order.Add(key);

                values[key] = pair[1];
            }

            return ObjectOf(order.Select(key => (key, values[key])));
        }
    }
}
