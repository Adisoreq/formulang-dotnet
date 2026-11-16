using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;

namespace WebPreview.Model
{
    public enum TokenClass { Comment, Text, Number, Variable, Environment, Function, Procedure, Constant, Keyword, Parameter, Operator, Bracket, Plain }

    public readonly record struct HighlightToken(string Text, TokenClass Class);

    /// <summary>Names known to the highlighter.</summary>
    public sealed record Vocabulary(IReadOnlySet<string> Functions, IReadOnlySet<string> Procedures, IReadOnlySet<string> Constants);

    public enum SuggestionKind { Function, Procedure, Constant, Variable, Environment, Keyword }

    /// <param name="Label">Text inserted into the formula.</param>
    public sealed record Suggestion(string Label, SuggestionKind Kind, string Detail);

    /// <summary>
    /// Syntax highlighting and suggestions of the editor. The lexer is tolerant: it highlights incomplete formulas
    /// (an unclosed text, an unknown character) that the tokenizer of the library would reject.
    /// </summary>
    public static partial class Language
    {
        private static readonly string[] Keywords = ["true", "false", "null"];

        // \G anchors every rule at the current position, like the sticky flag of JavaScript; \w and \d of .NET match
        // other scripts too, so the classes are written out
        private static readonly (Regex Pattern, TokenClass? Class)[] Rules =
        [
            (new Regex(@"\G#[^\n]*"), TokenClass.Comment),
            (new Regex(@"\G""(?:[^""\\\n]|\\.)*""?"), TokenClass.Text),
            (new Regex(@"\G(?:0[xX][0-9a-fA-F]+|(?:[0-9][0-9_]*(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?)"), TokenClass.Number),
            (new Regex(@"\G\$[A-Za-z_][A-Za-z0-9_]*"), TokenClass.Variable),
            (new Regex(@"\G@[A-Za-z_][A-Za-z0-9_]*"), TokenClass.Environment),
            (new Regex(@"\G[A-Za-z_][A-Za-z0-9_]*"), null),
            (new Regex(@"\G(?::=|\?=|=>|&&|\|\||>=|<=|==|<>|!=|[-+*/%^=<>!.,:;])"), TokenClass.Operator),
            (new Regex(@"\G[()\[\]{}]"), TokenClass.Bracket),
        ];

        [GeneratedRegex("^[A-Z][A-Z0-9_]*$")]
        private static partial Regex ConstantName();

        [GeneratedRegex(@"\$[A-Za-z_][A-Za-z0-9_]*")]
        private static partial Regex VariableName();

        [GeneratedRegex(@"(?:[$@]?[A-Za-z_][A-Za-z0-9_]*|[$@])\z")]
        private static partial Regex WordAtEnd();

        public static Vocabulary VocabularyOf(IEnvironment environment, ContextDefinition context)
        {
            var functions = new HashSet<string>(StringComparer.Ordinal);
            var procedures = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fn in environment.Functions)
                (fn.FunctionType == FunctionType.Procedure ? procedures : functions).Add(fn.Name);

            var constants = new HashSet<string>(context.Constants.Select(c => c.Name), StringComparer.Ordinal);
            foreach (string key in environment.Keys)
            {
                if (environment.GetProperty(key).ContainerType == ContainerType.Constant)
                    constants.Add(key);
            }

            return new Vocabulary(functions, procedures, constants);
        }

        public static List<HighlightToken> Highlight(string formula, Vocabulary words)
        {
            var tokens = new List<HighlightToken>();
            int position = 0;

            while (position < formula.Length)
            {
                bool matched = false;
                foreach (var (pattern, cls) in Rules)
                {
                    Match match = pattern.Match(formula, position);
                    if (!match.Success || match.Length == 0)
                        continue;

                    string text = match.Value;
                    position += text.Length;
                    matched = true;

                    if (cls is TokenClass c)
                        tokens.Add(new(text, c));
                    else if (Keywords.Contains(text))
                        tokens.Add(new(text, TokenClass.Keyword));
                    else if (words.Procedures.Contains(text))
                        tokens.Add(new(text, TokenClass.Procedure));
                    else if (words.Functions.Contains(text))
                        tokens.Add(new(text, TokenClass.Function));
                    else if (words.Constants.Contains(text) || ConstantName().IsMatch(text))
                        tokens.Add(new(text, TokenClass.Constant));
                    else
                        tokens.Add(new(text, TokenClass.Parameter));
                    break;
                }

                if (!matched)
                {
                    tokens.Add(new(formula[position].ToString(), TokenClass.Plain));
                    position++;
                }
            }

            return tokens;
        }

        /// <summary>The highlighted formula as HTML: a <c>span</c> with a <c>tk-*</c> class for every token.</summary>
        public static string HighlightHtml(string formula, Vocabulary words)
        {
            var html = new StringBuilder();
            foreach (HighlightToken token in Highlight(formula, words))
            {
                string text = WebUtility.HtmlEncode(token.Text);
                if (token.Class == TokenClass.Plain)
                    html.Append(text);
                else
                    html.Append("<span class=\"tk-").Append(Names.Css(token.Class)).Append("\">").Append(text).Append("</span>");
            }
            return html.ToString();
        }

        // Suggestions

        /// <summary>Everything a formula of the context type can refer to, except its own variables.</summary>
        public static List<Suggestion> SuggestionsOf(IEnvironment environment, ContextDefinition context)
        {
            var items = new List<Suggestion>();
            foreach (var fn in environment.Functions)
            {
                bool procedure = fn.FunctionType == FunctionType.Procedure;
                items.Add(new(fn.Name, procedure ? SuggestionKind.Procedure : SuggestionKind.Function,
                    string.Join("  ·  ", fn.Overloads.Select(o => $"{fn.Name}{o}"))));
            }

            foreach (ContextConstant c in context.Constants)
                items.Add(new(c.Name, SuggestionKind.Constant, c.Detail));

            foreach (string key in environment.Keys)
            {
                IContainer container = environment.GetProperty(key);
                if (container.ContainerType == ContainerType.Constant)
                    items.Add(new(key, SuggestionKind.Constant, $"environment constant = {container.GetValue()}"));
                else
                    items.Add(new($"@{key}", SuggestionKind.Environment, $"environment variable, default {container.GetValue()}"));
            }

            foreach (string keyword in Keywords)
                items.Add(new(keyword, SuggestionKind.Keyword, "literal"));

            return items;
        }

        /// <summary>The variables of the formula (<c>$name</c>), in the order of their first use.</summary>
        public static IEnumerable<Suggestion> VariablesOf(string formula)
            => VariableName().Matches(formula).Select(m => m.Value).Distinct(StringComparer.Ordinal)
                .Select(name => new Suggestion(name, SuggestionKind.Variable, "context variable of the formula"));

        /// <summary>The word being typed before the caret, with its sigil: <c>$va</c>, <c>@arr</c>, <c>SU</c>.</summary>
        public static (int Start, string Word) WordBefore(string formula, int caret)
        {
            caret = Math.Clamp(caret, 0, formula.Length);
            Match match = WordAtEnd().Match(formula[..caret]);
            return match.Success ? (caret - match.Length, match.Value) : (caret, "");
        }

        /// <summary>Suggestions that contain the phrase (case-insensitive); the ones that start with it first.</summary>
        public static List<Suggestion> Filter(IEnumerable<Suggestion> items, string phrase, int limit = 12)
        {
            if (phrase.Length == 0)
                return [];

            string needle = phrase.ToLowerInvariant();
            string sigil = phrase[0] is '$' or '@' ? phrase[..1] : "";

            var matches = items.Where(item =>
            {
                string label = item.Label.ToLowerInvariant();
                if (sigil != "" && !label.StartsWith(sigil, StringComparison.Ordinal))
                    return false;
                // A word typed in full is still offered for a function (it inserts the parenthesis), not for other words
                bool callable = item.Kind is SuggestionKind.Function or SuggestionKind.Procedure;
                return label.Contains(needle, StringComparison.Ordinal) && (label != needle || callable);
            });

            return matches
                .OrderBy(item => item.Label.ToLowerInvariant().StartsWith(needle, StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(item => item.Label, StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.None))
                .DistinctBy(item => item.Label)
                .Take(limit)
                .ToList();
        }
    }
}
