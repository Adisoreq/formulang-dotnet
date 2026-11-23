using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Functions;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Utils;
using FormuLang.NET.Core.Validation;
using System.Text;
using static FormuLang.NET.Core.Standards.FunctionHelpers;

namespace FormuLang.NET.Core.Standards
{
    public static partial class CoreFunctions
    {
        /// <summary>
        /// docs/standards/core/functions/FORMAT.md
        /// </summary>
        private static Function FormatFunction() => Fn("FORMAT",
            Overload((a, c) => Format(Txt(a[0]), a[1], c), TextArg("template"), ArrayArg("values")),
            Overload((a, c) => Format(Txt(a[0]), a[1], c), TextArg("template"), ObjectArg("values")));

        private enum SelectorKind { Sequential, Index, Key }

        /// <summary>
        /// Literal part of the template, or a placeholder with its selector and its optional specifier.
        /// </summary>
        private readonly record struct TemplatePart(string? Literal, string Selector, string? Specifier);

        private static IValue Format(string template, IValue values, IContext context)
        {
            if (ParseTemplate(template) is not List<TemplatePart> parts)
                return OperationError("The template has a \"{\" without a closing \"}\", or a single \"}\".");

            bool isArray = values.Type.Base == Types.ArrayName;
            bool sequential = false, indexed = false;

            foreach (TemplatePart part in parts.Where(p => p.Literal == null))
            {
                switch (Classify(part.Selector))
                {
                    case SelectorKind.Sequential when isArray:
                        sequential = true;
                        break;
                    case SelectorKind.Index when isArray:
                        indexed = true;
                        break;
                    case SelectorKind.Key when !isArray:
                        break;
                    default:
                        return OperationError($"Placeholder \"{{{part.Selector}}}\" does not match the type of \"values\".");
                }
            }

            if (sequential && indexed)
                return OperationError("A template uses either {} or {index} placeholders, not both.");

            var result = new StringBuilder(template.Length);
            int next = 0;

            foreach (TemplatePart part in parts)
            {
                if (part.Literal != null)
                {
                    result.Append(part.Literal);
                    continue;
                }

                IValue value;

                if (isArray)
                {
                    IValue[] elements = Arr(values);

                    if (part.Selector.Length == 0)
                    {
                        if (next >= elements.Length)
                            return InvalidIndexError("The template has more {} placeholders than elements of the array.");

                        value = elements[next++];
                    }
                    else if (TryResolveIndex(double.Parse(part.Selector, System.Globalization.CultureInfo.InvariantCulture),
                        elements.Length, context, out int index, out IValue? error))
                        value = elements[index];
                    else
                        return error!;
                }
                else
                {
                    IObject obj = Obj(values);
                    if (!obj.HasKey(part.Selector))
                        return Value.Error(Errors.InvalidKeyError($"Key \"{part.Selector}\" does not exist."), "");

                    value = obj.GetProperty(part.Selector).GetValue();
                }

                if (IsError(value))
                    return value;

                if (part.Specifier == null)
                {
                    result.Append(Serialize(value, context));
                    continue;
                }

                if (!TryParseDecimalPlaces(part.Specifier, out int places))
                    return OperationError($"\"{part.Specifier}\" is not a valid format specifier.");

                if (value.Raw is not double number || value.Type.Base != Types.NumberName)
                    return OperationError($"The specifier \"{part.Specifier}\" can be used only with a Number.");

                result.Append(StringConverter.NumberToString(number, places));
            }

            return T(result.ToString());
        }

        /// <summary>
        /// Splits the template into literals and placeholders, or returns <c>null</c> for an unclosed <c>{</c> or a
        /// single <c>}</c>.
        /// </summary>
        private static List<TemplatePart>? ParseTemplate(string template)
        {
            var parts = new List<TemplatePart>();
            var literal = new StringBuilder();

            void FlushLiteral()
            {
                if (literal.Length > 0)
                    parts.Add(new TemplatePart(literal.ToString(), "", null));
                literal.Clear();
            }

            for (int i = 0; i < template.Length; i++)
            {
                char c = template[i];
                char? next = i + 1 < template.Length ? template[i + 1] : null;

                if ((c == '{' && next == '{') || (c == '}' && next == '}'))
                {
                    literal.Append(c);
                    i++;
                }
                else if (c == '{')
                {
                    int end = template.IndexOf('}', i + 1);
                    if (end < 0)
                        return null;

                    string content = template[(i + 1)..end];
                    int colon = content.IndexOf(':');

                    FlushLiteral();
                    parts.Add(colon < 0
                        ? new TemplatePart(null, content, null)
                        : new TemplatePart(null, content[..colon], content[(colon + 1)..]));
                    i = end;
                }
                else if (c == '}')
                    return null;
                else
                    literal.Append(c);
            }

            FlushLiteral();
            return parts;
        }

        private static SelectorKind? Classify(string selector)
        {
            if (selector.Length == 0)
                return SelectorKind.Sequential;

            string digits = selector.StartsWith('-') ? selector[1..] : selector;
            if (digits.Length > 0 && digits.All(char.IsAsciiDigit))
                return SelectorKind.Index;

            return IdentifierValidator.Validate(selector) ? SelectorKind.Key : null;
        }

        /// <summary>
        /// The <c>.n</c> specifier: <c>n</c> is a whole number from 0.
        /// </summary>
        private static bool TryParseDecimalPlaces(string specifier, out int places)
        {
            places = 0;
            return specifier.Length > 1 && specifier[0] == '.' && specifier[1..].All(char.IsAsciiDigit)
                && int.TryParse(specifier[1..], out places) && places <= 100;
        }
    }
}
