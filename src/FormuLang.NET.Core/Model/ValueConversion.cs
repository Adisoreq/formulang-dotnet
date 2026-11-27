using System.Collections;
using System.Globalization;
using System.Text.Json;
using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Utils;

namespace FormuLang.NET.Core.Model
{
    /// <summary>
    /// Conversion between the values of .NET (numbers, texts, Booleans, lists, dictionaries, <see cref="JsonElement"/>s)
    /// and FormuLang values, e.g. for the parameter values given by a host application
    /// (docs/implementation/formula_models.md, "Parameter normalization").
    /// </summary>
    public static class ValueConversion
    {
        /// <summary>
        /// A .NET value as a FormuLang value: <c>null</c> → Null, numbers → Number, <see cref="string"/> → Text,
        /// <see cref="bool"/> → Boolean, <see cref="JsonElement"/>, an <see cref="IValue"/> (unchanged), a dictionary with
        /// <see cref="string"/> keys → Object, any other enumerable → Array.
        /// </summary>
        /// <returns><c>false</c> for a value that cannot be converted (e.g. a delegate or a non-finite number).</returns>
        public static bool TryFromClr(object? value, out IValue result)
        {
            switch (value)
            {
                case null:
                    result = Value.Null("null");
                    return true;
                case IValue flValue:
                    result = flValue;
                    return true;
                case bool b:
                    result = Value.Boolean(b, b ? "true" : "false");
                    return true;
                case string s:
                    result = Value.Text(s, StringConverter.TextToLiteral(s));
                    return true;
                case JsonElement json:
                    return TryFromJson(json, out result);
                case double or float or decimal or int or long or short or byte or sbyte or uint or ulong or ushort:
                    double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    result = Value.Number(number == 0 ? 0 : number);
                    return double.IsFinite(number);
                case IDictionary dictionary:
                    var obj = new Primitives.Object();

                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (entry.Key is not string key || !TryFromClr(entry.Value, out IValue element))
                        {
                            result = Value.Null("null");
                            return false;
                        }

                        obj.InitVariable(key, element);
                    }

                    result = Value.Object(obj, "");
                    return true;
                case IEnumerable<KeyValuePair<string, object?>> pairs:
                    var pairObject = new Primitives.Object();

                    foreach (var (key, item) in pairs)
                    {
                        if (!TryFromClr(item, out IValue element))
                        {
                            result = Value.Null("null");
                            return false;
                        }

                        pairObject.InitVariable(key, element);
                    }

                    result = Value.Object(pairObject, "");
                    return true;
                case IEnumerable enumerable:
                    var elements = new List<IValue>();

                    foreach (object? item in enumerable)
                    {
                        if (!TryFromClr(item, out IValue element))
                        {
                            result = Value.Null("null");
                            return false;
                        }

                        elements.Add(element);
                    }

                    result = Value.Array(elements, "");
                    return true;
                default:
                    result = Value.Null("null");
                    return false;
            }
        }

        /// <summary>
        /// A .NET value as a FormuLang value (<see cref="TryFromClr"/>).
        /// </summary>
        /// <exception cref="ArgumentException">The value cannot be converted.</exception>
        public static IValue FromClr(object? value)
            => TryFromClr(value, out IValue result) ? result : throw new ArgumentException($"A value of type {value?.GetType().Name} cannot be converted to a FormuLang value.", nameof(value));

        /// <summary>
        /// A JSON value as a FormuLang value.
        /// </summary>
        public static bool TryFromJson(JsonElement json, out IValue result)
        {
            switch (json.ValueKind)
            {
                case JsonValueKind.Null:
                    result = Value.Null("null");
                    return true;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    bool b = json.GetBoolean();
                    result = Value.Boolean(b, b ? "true" : "false");
                    return true;
                case JsonValueKind.Number:
                    result = Value.Number(json.GetDouble());
                    return true;
                case JsonValueKind.String:
                    string text = json.GetString()!;
                    result = Value.Text(text, StringConverter.TextToLiteral(text));
                    return true;
                case JsonValueKind.Array:
                    var elements = new List<IValue>();

                    foreach (JsonElement item in json.EnumerateArray())
                    {
                        if (!TryFromJson(item, out IValue element))
                        {
                            result = Value.Null("null");
                            return false;
                        }

                        elements.Add(element);
                    }

                    result = Value.Array(elements, "");
                    return true;
                case JsonValueKind.Object:
                    var obj = new Primitives.Object();

                    foreach (JsonProperty property in json.EnumerateObject())
                    {
                        if (!TryFromJson(property.Value, out IValue element))
                        {
                            result = Value.Null("null");
                            return false;
                        }

                        obj.InitVariable(property.Name, element);
                    }

                    result = Value.Object(obj, "");
                    return true;
                default:
                    result = Value.Null("null");
                    return false;
            }
        }

        /// <summary>
        /// A FormuLang value as a .NET value: Null → <c>null</c>, Number → <see cref="double"/>, Text →
        /// <see cref="string"/>, Boolean → <see cref="bool"/>, Array → <see cref="List{T}"/>, Object (also of an extended
        /// type) → <see cref="Dictionary{TKey, TValue}"/> in the order of the keys, an Error or a Delegate →
        /// its serialization.
        /// </summary>
        public static object? ToClr(IValue value) => value.Raw switch
        {
            null => null,
            double d => d,
            bool b => b,
            string s => s,
            IValue[] array => array.Select(ToClr).ToList(),
            IObject obj => obj.Keys.ToDictionary(key => key, key => ToClr(obj.GetProperty(key).GetValue()), StringComparer.Ordinal),
            _ => StringConverter.ValueToString(value),
        };

        /// <summary>
        /// A FormuLang value as a JSON value (<see cref="ToClr"/>).
        /// </summary>
        public static JsonElement ToJson(IValue value) => JsonSerializer.SerializeToElement(ToClr(value));
    }
}
