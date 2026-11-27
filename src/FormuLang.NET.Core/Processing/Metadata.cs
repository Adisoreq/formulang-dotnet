using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Processing;
using FormuLang.NET.Core.Primitives;

namespace FormuLang.NET.Core.Processing
{
    /// <summary>
    /// Result of the metadata reading (docs/core/metadata.md, "Metadata reading", since <c>core-1.2</c>).
    /// </summary>
    public sealed class MetadataResult : IMetadataResult
    {
        public IValue Metadata { get; }
        public string Formula { get; }
        public IError Error { get; }

        /// <summary>
        /// Position of the <c>&lt;</c> of the metadata block, or <c>null</c> if the formula has no block.
        /// </summary>
        public SourcePosition? Position { get; }

        public bool Successful => Error.IsSuccess();

        /// <summary>
        /// Whether the formula has a metadata block.
        /// </summary>
        public bool HasBlock => Position != null;

        /// <summary>
        /// The metadata Object (<see cref="Metadata"/> as an Object).
        /// </summary>
        public IObject Object => (IObject)Metadata.Raw!;

        public MetadataResult(IValue metadata, string formula, SourcePosition? position, IError? error = null)
        {
            Metadata = metadata;
            Formula = formula;
            Position = position;
            Error = error ?? Errors.Success();
        }
    }

    /// <summary>
    /// Metadata reading (docs/core/metadata.md, "Metadata reading", since <c>core-1.2</c>): reads the metadata block
    /// <c>&lt;@ { ... } @&gt;</c> at the beginning of a formula, if there is one, and replaces it with white space. A block
    /// that is not closed, or whose content is not one Object literal of constant values, is a <c>SYNTAX_ERROR</c>.
    /// </summary>
    /// <remarks>
    /// The block is replaced character by character (a space for every character, a line break stays), so the lines and
    /// columns of the rest of the formula do not change; the result is the same as with the one new line or space of the
    /// standard, because the clean-up removes empty lines and repeated spaces. The formula is expected to be validated;
    /// the reading needs no environment and no context.
    /// </remarks>
    public static class MetadataReader
    {
        private const string Opening = "<@";
        private const string Closing = "@>";

        private const string NotConstant =
            "may contain only constant values: Number, Text and Boolean literals, null, and Arrays and Objects of them";

        public static MetadataResult Read(string formula)
        {
            ArgumentNullException.ThrowIfNull(formula);

            int start = SkipLeadingTrivia(formula);

            if (string.CompareOrdinal(formula, start, Opening, 0, Opening.Length) != 0)
                return new MetadataResult(EmptyMetadata(), formula, null);

            SourcePosition position = PositionOf(formula, start);
            string at = $"(line {position.Line}, column {position.Column})";
            MetadataResult Fail(string message) => new(EmptyMetadata(), formula, position, Errors.SyntaxError(message));

            int end = FindClosing(formula, start);
            if (end < 0)
                return Fail($"The metadata block is not closed {at}.");

            string content = formula[(start + Opening.Length)..end];
            string mustBeObject = $"The metadata block {at} must contain one Object literal.";

            CleanUpResult cleanUp = Cleaner.CleanUp(content);
            if (cleanUp.Error.IsFailed())
                return Fail(mustBeObject);

            TokenizationResult tokenization = Tokenizer.Tokenize(cleanUp.Result);
            if (tokenization.Error.IsFailed())
                return Fail($"The metadata block {at}: {tokenization.Error.Message}");

            ParsingResult parsing = new Parser().Parse(tokenization.Tokens);
            if (parsing.Error.IsFailed())
                return Fail($"The metadata block {at}: {parsing.Error.Message}");

            if (parsing.Sequences.Count != 1 || parsing.Sequences[0].Root is not CallNode { Name: "OBJECT" } root)
                return Fail(mustBeObject);

            if (ConstantValue(root) is not IValue metadata)
                return Fail($"The metadata block {at} {NotConstant}.");

            var block = new char[end + Closing.Length - start];
            for (int i = 0; i < block.Length; i++)
            {
                char c = formula[start + i];
                block[i] = c == '\r' || c == '\n' ? c : ' ';
            }

            string replaced = string.Concat(formula.AsSpan(0, start), new string(block), formula.AsSpan(end + Closing.Length));
            return new MetadataResult(metadata, replaced, position);
        }

        private static IValue EmptyMetadata() => Value.Object(new Primitives.Object(), "{}");

        /// <summary>
        /// Line and column (in code points) of an index of the text.
        /// </summary>
        private static SourcePosition PositionOf(string text, int index)
        {
            int line = 1, column = 1;

            for (int i = 0; i < index; i++)
            {
                char c = text[i];

                if (c == '\r' || c == '\n')
                {
                    if (c == '\n' && i > 0 && text[i - 1] == '\r')
                        continue;

                    line++;
                    column = 1;
                }
                else if (!char.IsLowSurrogate(c))
                {
                    column++;
                }
            }

            return new SourcePosition(line, column);
        }

        /// <summary>
        /// Index of the end of the white space and the comments at the beginning of the formula.
        /// </summary>
        private static int SkipLeadingTrivia(string formula)
        {
            int i = 0;

            while (i < formula.Length)
            {
                char c = formula[i];

                if (c is ' ' or '\t' or '\n' or '\r')
                {
                    i++;
                }
                else if (c == '#')
                {
                    while (i < formula.Length && formula[i] != '\n' && formula[i] != '\r')
                        i++;
                }
                else if (c == '<' && i + 1 < formula.Length && formula[i + 1] == '#')
                {
                    int end = formula.IndexOf("#>", i + 2, StringComparison.Ordinal);
                    if (end < 0)
                        return formula.Length;

                    i = end + 2;
                }
                else
                {
                    break;
                }
            }

            return i;
        }

        /// <summary>
        /// Index of the <c>@</c> of the <c>@&gt;</c> that closes the block opened at <paramref name="start"/>: the first one
        /// outside of a text literal and of a comment; <c>-1</c> if there is none.
        /// </summary>
        private static int FindClosing(string formula, int start)
        {
            bool insideText = false, escape = false;

            for (int i = start + Opening.Length; i < formula.Length; i++)
            {
                char c = formula[i];

                if (insideText)
                {
                    if (c is '\n' or '\r')
                        insideText = false;
                    else if (escape)
                        escape = false;
                    else if (c == '\\')
                        escape = true;
                    else if (c == '"')
                        insideText = false;

                    continue;
                }

                if (c == '"')
                {
                    insideText = true;
                }
                else if (c == '#')
                {
                    while (i + 1 < formula.Length && formula[i + 1] != '\n' && formula[i + 1] != '\r')
                        i++;
                }
                else if (c == '<' && i + 1 < formula.Length && formula[i + 1] == '#')
                {
                    int end = formula.IndexOf("#>", i + 2, StringComparison.Ordinal);
                    if (end < 0)
                        return -1;

                    i = end + 1;
                }
                else if (c == '@' && i + 1 < formula.Length && formula[i + 1] == '>')
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// The value of a constant node of the metadata block, or <c>null</c> for a node that is not constant.
        /// </summary>
        private static IValue? ConstantValue(ExpressionNode node)
        {
            switch (node)
            {
                case LiteralNode literal:
                    return literal.Value;

                case CallNode { Name: "NEG", Children: [LiteralNode { Value.Raw: double number }] }:
                    double negated = -number;
                    return Value.Number(negated == 0 ? 0 : negated);

                case CallNode { Name: "ARRAY" } array:
                    var elements = new List<IValue>();

                    foreach (ExpressionNode child in array.Children)
                    {
                        if (ConstantValue(child) is not IValue element)
                            return null;

                        elements.Add(element);
                    }

                    return Value.Array(elements, "");

                case CallNode { Name: "OBJECT" } obj:
                    var result = new Primitives.Object();

                    for (int i = 0; i + 1 < obj.Children.Count; i += 2)
                    {
                        if (obj.Children[i] is not LiteralNode { Value.Raw: string key } || ConstantValue(obj.Children[i + 1]) is not IValue value)
                            return null;

                        result.InitVariable(key, value);
                    }

                    return Value.Object(result, "");

                default:
                    return null;
            }
        }
    }
}
