using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Processing;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Functions;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Structures;

namespace FormuLang.NET.Core.Processing
{
    /// <summary>
    /// Immutable warning of the preparation (docs/standards/core/semantics/warnings.md, since <c>core-1.2</c>).
    /// </summary>
    public sealed record Warning(string Code, string Message, int Line, int Column) : IWarning
    {
        /// <summary>
        /// <c>UNUSED_VALUE (line 2, column 1): The value of ...</c>
        /// </summary>
        public override string ToString() => $"{Code} (line {Line}, column {Column}): {Message}";
    }

    /// <summary>
    /// Reports a value of a wrong type: the key (dotted for a nested key, e.g. <c>parameters.period.min</c>) and the type.
    /// </summary>
    public delegate void MetadataReport(string key, string type);

    /// <summary>
    /// Checks the value of a top-level metadata key of an extension standard or of the host application (e.g.
    /// <c>stock</c>), registered with <see cref="Structures.Environment.RegisterMetadataChecker"/>.
    /// </summary>
    public delegate void MetadataChecker(IValue value, string key, MetadataReport report);

    /// <summary>
    /// Building blocks of the <see cref="MetadataChecker"/>s.
    /// </summary>
    public static class MetadataChecks
    {
        private static bool Is(IValue value, string type) => value.Type.Base == type;

        /// <summary>A Text.</summary>
        public static void Text(IValue value, string key, MetadataReport report)
        {
            if (!Is(value, Types.TextName))
                report(key, Types.TextName);
        }

        /// <summary>A Number.</summary>
        public static void Number(IValue value, string key, MetadataReport report)
        {
            if (!Is(value, Types.NumberName))
                report(key, Types.NumberName);
        }

        /// <summary>An Array.</summary>
        public static void Array(IValue value, string key, MetadataReport report)
        {
            if (!Is(value, Types.ArrayName))
                report(key, Types.ArrayName);
        }

        /// <summary>A Text, or an Array of Texts.</summary>
        public static void TextOrTexts(IValue value, string key, MetadataReport report)
        {
            bool valid = Is(value, Types.TextName)
                || (Is(value, Types.ArrayName) && ((IValue[])value.Raw!).All(e => Is(e, Types.TextName)));

            if (!valid)
                report(key, "Text or Array of Text");
        }

        /// <summary>
        /// An Object whose known keys are checked by <paramref name="keys"/>; other keys are ignored.
        /// </summary>
        public static MetadataChecker Object(IReadOnlyDictionary<string, MetadataChecker> keys) => (value, key, report) =>
        {
            if (!Is(value, Types.ObjectName))
            {
                report(key, Types.ObjectName);
                return;
            }

            var obj = (IObject)value.Raw!;

            foreach (var (name, check) in keys)
            {
                if (obj.HasKey(name))
                    check(obj.GetProperty(name).GetValue(), $"{key}.{name}", report);
            }
        };

        /// <summary>
        /// An Object whose every value is checked by <paramref name="check"/>.
        /// </summary>
        public static MetadataChecker ObjectOf(MetadataChecker check) => (value, key, report) =>
        {
            if (!Is(value, Types.ObjectName))
            {
                report(key, Types.ObjectName);
                return;
            }

            var obj = (IObject)value.Raw!;

            foreach (string name in obj.Keys)
                check(obj.GetProperty(name).GetValue(), $"{key}.{name}", report);
        };
    }

    /// <summary>
    /// The warnings of the preparation (docs/standards/core/semantics/warnings.md): <c>UNUSED_VALUE</c> and
    /// <c>CONTINUED_LINE</c> of the sequences, <c>METADATA_TYPE</c> and <c>METADATA_CONTEXT</c> of the metadata.
    /// </summary>
    public static class Warnings
    {
        // Order of the warnings at the same position: the order of the table of the standard
        private static readonly string[] Order =
            [WarningCode.UnusedValue, WarningCode.ContinuedLine, WarningCode.MetadataType, WarningCode.MetadataContext];

        /// <summary>
        /// Warnings in the order of their positions; warnings at the same position in the order of the standard.
        /// </summary>
        public static IReadOnlyList<IWarning> Sort(IEnumerable<IWarning> warnings)
        {
            static int Rank(string code)
            {
                int index = System.Array.IndexOf(Order, code);
                return index < 0 ? Order.Length : index;
            }

            return warnings.OrderBy(w => w.Line).ThenBy(w => w.Column).ThenBy(w => Rank(w.Code)).ToArray();
        }

        // Sequences

        /// <summary>
        /// <c>UNUSED_VALUE</c> and <c>CONTINUED_LINE</c>. A sequence is not an unused value if its root is a procedure call
        /// (an assignment or another procedure) or a function with side effects (<see cref="Function.SideEffectFree"/> is
        /// <c>false</c>, e.g. a function of the host application).
        /// </summary>
        public static List<IWarning> SequenceWarnings(IParsingResult parsing, IEnvironment environment)
        {
            var warnings = new List<IWarning>();
            IReadOnlyList<SequenceStart>? starts = (parsing as ParsingResult)?.Starts;
            IReadOnlyList<ISequence> sequences = parsing.Sequences;

            for (int i = 0; i < sequences.Count; i++)
            {
                SequenceStart? start = starts != null && i < starts.Count ? starts[i] : null;
                int line = start?.Line ?? 0;
                int column = start?.First.Column ?? 0;

                if (i < sequences.Count - 1 && !HasEffect(sequences[i].Root, environment))
                    warnings.Add(new Warning(WarningCode.UnusedValue, $"The value of the sequence at line {line} is not used.", line, column));

                IToken? first = start?.First;

                if (i > 0 && first is { Type: TokenType.Operator, Value: "-" or "!" } && start!.Separator?.Value == "\n"
                    && OnPreviousLine(start.PreviousLast?.Line, first.Line))
                {
                    warnings.Add(new Warning(WarningCode.ContinuedLine,
                        $"The sequence at line {line} starts with \"{first.Value}\": a new line ends the previous sequence. " +
                        "Put the operator at the end of the previous line, or the expression in parentheses.", line, column));
                }
            }

            return warnings;
        }

        private static bool OnPreviousLine(int? previous, int? current)
            => previous == null || current == null || previous == current - 1;

        private static bool HasEffect(IExpressionNode root, IEnvironment environment)
        {
            if (root is not CallNode call)
                return false;

            // An unknown function is an Error of the evaluation, not a warning
            return environment.GetFunction(call.Name) switch
            {
                null => true,
                Procedure => true,
                Function function => !function.SideEffectFree,
                _ => true,
            };
        }

        // Metadata

        private static readonly MetadataChecker ParameterDescription = MetadataChecks.Object(new Dictionary<string, MetadataChecker>
        {
            ["type"] = MetadataChecks.Text,
            ["name"] = MetadataChecks.Text,
            ["description"] = MetadataChecks.Text,
            ["min"] = MetadataChecks.Number,
            ["max"] = MetadataChecks.Number,
            ["options"] = MetadataChecks.Array,
        });

        // The metadata keys of the Core standard
        private static readonly Dictionary<string, MetadataChecker> CoreKeys = new(StringComparer.Ordinal)
        {
            ["id"] = MetadataChecks.Text,
            ["name"] = MetadataChecks.Text,
            ["description"] = MetadataChecks.Text,
            ["author"] = MetadataChecks.Text,
            ["version"] = MetadataChecks.Text,
            ["context"] = MetadataChecks.Text,
            ["result"] = MetadataChecks.TextOrTexts,
            ["requires"] = MetadataChecks.TextOrTexts,
            ["parameters"] = MetadataChecks.ObjectOf(ParameterDescription),
        };

        /// <summary>
        /// The <c>METADATA_TYPE</c> warnings of a metadata Object: the Core keys, and the keys checked by
        /// <paramref name="checkers"/> (e.g. the <c>stock</c> key of an extension standard). It needs no context and no
        /// environment.
        /// </summary>
        public static List<IWarning> MetadataTypeWarnings(IValue metadata, SourcePosition position, Func<string, MetadataChecker?>? checkers = null)
        {
            var warnings = new List<IWarning>();

            if (metadata.Raw is not IObject obj)
                return warnings;

            void Report(string key, string type)
                => warnings.Add(new Warning(WarningCode.MetadataType, $"Metadata key \"{key}\" must be of type {type}.", position.Line, position.Column));

            foreach (string key in obj.Keys)
            {
                MetadataChecker? check = CoreKeys.GetValueOrDefault(key) ?? checkers?.Invoke(key);
                check?.Invoke(obj.GetProperty(key).GetValue(), key, Report);
            }

            return warnings;
        }

        /// <summary>
        /// <c>METADATA_TYPE</c> (with the checkers of the environment) and <c>METADATA_CONTEXT</c>, at the position of the
        /// metadata block.
        /// </summary>
        public static List<IWarning> MetadataWarnings(IValue metadata, SourcePosition position, IContext context)
        {
            var environment = context.Environment as Structures.Environment;
            List<IWarning> warnings = MetadataTypeWarnings(metadata, position, key => environment?.GetMetadataChecker(key));

            if (metadata.Raw is not IObject obj || !obj.HasKey("context"))
                return warnings;

            IReadOnlyList<string>? names = (context as Context)?.ContextTypeNames;

            if (obj.GetProperty("context").GetValue().Raw is string declared && names is { Count: > 0 } && !names.Contains(declared))
            {
                warnings.Add(new Warning(WarningCode.MetadataContext,
                    $"The formula is written for the context \"{declared}\", but is executed in \"{names[0]}\".", position.Line, position.Column));
            }

            return warnings;
        }
    }
}
