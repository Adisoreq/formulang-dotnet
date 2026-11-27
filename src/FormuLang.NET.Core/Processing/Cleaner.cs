using FormuLang.NET.Abstractions.Processing;
using FormuLang.NET.Core.Utils;
using System.Text;

namespace FormuLang.NET.Core.Processing
{
    /// <summary>
    /// Position of a character in the original formula: line and column (in Unicode code points), both from 1.
    /// </summary>
    public readonly record struct SourcePosition(int Line, int Column)
    {
        public override string ToString() => $"line {Line}, column {Column}";
    }

    /// <summary>
    /// The positions of the characters of a cleaned formula in the original formula
    /// (<see cref="Cleaner.CleanUpWithPositions"/>): for every line of the cleaned formula, the position of every UTF-16
    /// code unit of the line.
    /// </summary>
    public sealed class CleanUpPositions
    {
        private readonly IReadOnlyList<SourcePosition[]> _lines;

        internal CleanUpPositions(IReadOnlyList<SourcePosition[]> lines)
        {
            _lines = lines;
        }

        /// <summary>
        /// The original position of the character at <paramref name="column"/> (from 1, in UTF-16 code units) of the
        /// cleaned <paramref name="line"/>; a column after the end of the line is one column after its last character.
        /// <c>null</c> for a line that does not exist.
        /// </summary>
        public SourcePosition? At(int line, int column)
        {
            if (line < 1 || line > _lines.Count || _lines[line - 1].Length == 0)
                return null;

            SourcePosition[] positions = _lines[line - 1];

            if (column >= 1 && column <= positions.Length)
                return positions[column - 1];

            SourcePosition last = positions[^1];
            return new SourcePosition(last.Line, last.Column + 1);
        }
    }

    /// <summary>
    /// Clean-up stage (docs/semantics/cleanup.md): normalization, comment removal,
    /// white space compression and line compression.
    /// </summary>
    public class Cleaner : ICleaner
    {
        protected const char TextDelimiter = '"';
        protected const char EscapeSymbol = '\\';
        protected const char CommentSigil = '#';
        protected const char ScopedCommentOpening = '<';
        protected const string ScopedCommentEnd = "#>";
        private const char NewLine = '\n';

        /// <summary>
        /// A UTF-16 code unit of the formula with the position of its code point in the original text.
        /// </summary>
        private readonly record struct Char(char C, SourcePosition Position);

        /// <summary>
        /// Part of a line: either a text literal (with its quotation marks) or code outside text literals.
        /// </summary>
        private readonly record struct Segment(List<Char> Content, bool IsText);

        /// <summary>
        /// 1. Normalization: the characters with their positions; tabs become spaces and line breaks become <c>\n</c>.
        /// </summary>
        private static List<Char> Normalize(string expression)
        {
            var chars = new List<Char>(expression.Length);
            int line = 1, column = 0;

            for (int i = 0; i < expression.Length; i++)
            {
                char c = expression[i];

                if (c == '\n' && i > 0 && expression[i - 1] == '\r')
                    continue;

                // The low surrogate of a pair is a part of the code point of the high surrogate
                if (!(char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(expression[i - 1])))
                    column++;

                var position = new SourcePosition(line, column);

                if (c == '\n' || c == '\r')
                {
                    chars.Add(new Char(NewLine, position));
                    line++;
                    column = 0;
                }
                else
                {
                    chars.Add(new Char(c == '\t' ? ' ' : c, position));
                }
            }

            return chars;
        }

        /// <summary>
        /// Splits a line into text literals and code, and drops the comment.
        /// A text literal ends at the next quotation mark not preceded by an escaping backslash, or at the end of the line.
        /// </summary>
        private static List<Segment> SplitLine(List<Char> line)
        {
            var segments = new List<Segment>();
            var current = new List<Char>();

            bool insideText = false;
            bool escape = false;

            foreach (Char ch in line)
            {
                char c = ch.C;

                if (insideText)
                {
                    current.Add(ch);

                    if (escape)
                        escape = false;
                    else if (c == EscapeSymbol)
                        escape = true;
                    else if (c == TextDelimiter)
                    {
                        segments.Add(new Segment(current, true));
                        current = [];
                        insideText = false;
                    }

                    continue;
                }

                if (c == CommentSigil)
                    break;

                if (c == TextDelimiter)
                {
                    if (current.Count > 0)
                        segments.Add(new Segment(current, false));

                    current = [ch];
                    insideText = true;
                    continue;
                }

                current.Add(ch);
            }

            if (current.Count > 0)
                segments.Add(new Segment(current, insideText));

            return segments;
        }

        /// <summary>
        /// Replaces every scoped comment (<c>&lt;# ... #&gt;</c>) located outside a text literal and outside a line comment
        /// with one <c>[New line]</c> if it contains a line break, or with one <c>[Space]</c> otherwise
        /// (docs/semantics/cleanup.md, "Removing comments"). Line comments are left for <see cref="SplitLine"/>.
        /// A scoped comment that is not closed (reported by the validation) is removed up to the end of the formula.
        /// </summary>
        private static List<Char> RemoveScopedComments(List<Char> formula)
        {
            bool hasScopedComment = false;
            for (int i = 0; i + 1 < formula.Count && !hasScopedComment; i++)
                hasScopedComment = formula[i].C == ScopedCommentOpening && formula[i + 1].C == CommentSigil;

            if (!hasScopedComment)
                return formula;

            var result = new List<Char>(formula.Count);
            bool insideText = false, insideLineComment = false, escape = false;

            for (int i = 0; i < formula.Count; i++)
            {
                Char ch = formula[i];
                char c = ch.C;

                if (c == NewLine)
                {
                    insideText = insideLineComment = escape = false;
                    result.Add(ch);
                    continue;
                }

                if (insideLineComment)
                {
                    result.Add(ch);
                    continue;
                }

                if (insideText)
                {
                    result.Add(ch);

                    if (escape)
                        escape = false;
                    else if (c == EscapeSymbol)
                        escape = true;
                    else if (c == TextDelimiter)
                        insideText = false;

                    continue;
                }

                if (c == ScopedCommentOpening && i + 1 < formula.Count && formula[i + 1].C == CommentSigil)
                {
                    int stop = formula.Count;
                    bool multiline = false;

                    for (int j = i + 2; j + 1 < formula.Count; j++)
                    {
                        if (formula[j].C == ScopedCommentEnd[0] && formula[j + 1].C == ScopedCommentEnd[1])
                        {
                            stop = j + 2;
                            break;
                        }
                    }

                    for (int j = i; j < stop && !multiline; j++)
                        multiline = formula[j].C == NewLine;

                    result.Add(new Char(multiline ? NewLine : ' ', ch.Position));
                    i = stop - 1;
                    continue;
                }

                if (c == CommentSigil)
                    insideLineComment = true;
                else if (c == TextDelimiter)
                    insideText = true;

                result.Add(ch);
            }

            return result;
        }

        private static bool IsCompressibleOperator(char c)
            => StringUtils.IsCharOperator(c) && c != TextDelimiter;

        /// <summary>
        /// Compresses white spaces in code outside text literals:
        /// multiple spaces become one, and spaces next to an operator are removed.
        /// </summary>
        private static List<Char> CompressCode(List<Char> code)
        {
            var result = new List<Char>(code.Count);

            for (int i = 0; i < code.Count; i++)
            {
                Char ch = code[i];

                if (ch.C != ' ')
                {
                    result.Add(ch);
                    continue;
                }

                // Multi-space: keep only the last space of a run
                if (i + 1 < code.Count && code[i + 1].C == ' ')
                    continue;

                // Operator spaces
                if ((result.Count > 0 && IsCompressibleOperator(result[^1].C)) ||
                    (i + 1 < code.Count && IsCompressibleOperator(code[i + 1].C)))
                    continue;

                result.Add(ch);
            }

            return result;
        }

        private static List<Char> CleanLine(List<Char> line)
        {
            List<Segment> segments = SplitLine(line);
            var result = new List<Char>(line.Count);

            for (int i = 0; i < segments.Count; i++)
            {
                Segment segment = segments[i];

                if (segment.IsText)
                {
                    result.AddRange(segment.Content);
                    continue;
                }

                List<Char> code = CompressCode(segment.Content);
                int start = 0, end = code.Count;

                // Trim spaces at the start and end of the line (never inside a text literal)
                if (i == 0)
                    while (start < end && code[start].C == ' ') start++;
                if (i == segments.Count - 1)
                    while (end > start && code[end - 1].C == ' ') end--;

                result.AddRange(code.GetRange(start, end - start));
            }

            return result;
        }

        public static CleanUpResult CleanUp(string expression) => CleanUpWithPositions(expression).Result;

        /// <summary>
        /// <see cref="CleanUp"/>, and the position of every character of the cleaned formula in
        /// <paramref name="expression"/>, used for the positions of the warnings and of the parsing errors.
        /// </summary>
        public static (CleanUpResult Result, CleanUpPositions Positions) CleanUpWithPositions(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return (new CleanUpResult(string.Empty, Errors.OperationError("Input string is empty.")), new CleanUpPositions([]));

            // 1. Normalization; 2. comments: scoped comments first, line comments line by line
            var lines = new List<List<Char>> { new() };

            foreach (Char ch in RemoveScopedComments(Normalize(expression)))
            {
                if (ch.C == NewLine)
                    lines.Add([]);
                else
                    lines[^1].Add(ch);
            }

            // 3. White spaces; 4. line compression
            List<List<Char>> cleaned = lines.Select(CleanLine).Where(line => line.Count > 0).ToList();

            var text = new StringBuilder(expression.Length);
            for (int i = 0; i < cleaned.Count; i++)
            {
                if (i > 0)
                    text.Append(NewLine);

                foreach (Char ch in cleaned[i])
                    text.Append(ch.C);
            }

            var positions = cleaned.Select(line => line.Select(ch => ch.Position).ToArray()).ToArray();
            return (new CleanUpResult(text.ToString(), Errors.Success()), new CleanUpPositions(positions));
        }

        ICleanUpResult ICleaner.CleanUp(string expression) => CleanUp(expression);
    }
}
