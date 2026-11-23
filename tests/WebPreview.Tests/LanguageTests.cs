namespace WebPreview.Tests
{
    /// <summary>Highlighting and suggestions of the editor.</summary>
    public class LanguageTests
    {
        private static readonly ContextDefinition DefaultContext = Examples.Contexts.First(c => c.Kind == ContextKind.Default);
        private static readonly Vocabulary Words = Language.VocabularyOf(Engine.ReferenceEnvironment, DefaultContext);

        [Fact]
        public void HighlightsEveryKindOfToken()
        {
            var tokens = Language.Highlight("$r := ROUND(PI * 2, @x) # note", Words);

            Assert.Contains(new HighlightToken("$r", TokenClass.Variable), tokens);
            Assert.Contains(new HighlightToken(":=", TokenClass.Operator), tokens);
            Assert.Contains(new HighlightToken("ROUND", TokenClass.Function), tokens);
            Assert.Contains(new HighlightToken("PI", TokenClass.Constant), tokens);
            Assert.Contains(new HighlightToken("2", TokenClass.Number), tokens);
            Assert.Contains(new HighlightToken("@x", TokenClass.Environment), tokens);
            Assert.Contains(new HighlightToken("# note", TokenClass.Comment), tokens);
        }

        [Fact]
        public void HighlightingKeepsTheWholeFormula()
        {
            const string formula = "IF(true, \"a\\\"b\", null) + 0x1F ~ [1, 2]";
            Assert.Equal(formula, string.Concat(Language.Highlight(formula, Words).Select(t => t.Text)));
        }

        [Fact]
        public void UnclosedTextIsStillHighlighted()
            => Assert.Equal(TokenClass.Text, Language.Highlight("\"open", Words).Single().Class);

        [Fact]
        public void HtmlIsEncoded()
        {
            string html = Language.HighlightHtml("\"<b>\"", Words);
            Assert.Equal("<span class=\"tk-text\">&quot;&lt;b&gt;&quot;</span>", html);
        }

        [Theory]
        [InlineData("1 + $va", 7, 4, "$va")]
        [InlineData("SU", 2, 0, "SU")]
        [InlineData("1 + ", 4, 4, "")]
        [InlineData("@arr + 1", 4, 0, "@arr")]
        [InlineData("abc", 99, 0, "abc")]
        public void FindsWordBeforeCaret(string formula, int caret, int start, string word)
            => Assert.Equal((start, word), Language.WordBefore(formula, caret));

        [Fact]
        public void VariablesAreDistinctInOrderOfUse()
        {
            var names = Language.VariablesOf("$b := 1\n$a := $b + $b").Select(s => s.Label);
            Assert.Equal(["$b", "$a"], names);
        }

        [Fact]
        public void FilterPutsPrefixMatchesFirstAndRespectsLimit()
        {
            Suggestion[] items =
            [
                new("ASUM", SuggestionKind.Function, ""),
                new("SUM", SuggestionKind.Function, ""),
                new("SUMSQ", SuggestionKind.Function, ""),
                new("$sum", SuggestionKind.Variable, ""),
            ];

            // Without a sigil every kind matches; "$sum" and "ASUM" only contain the phrase, so they come last
            Assert.Equal(["SUM", "SUMSQ", "$sum", "ASUM"], Language.Filter(items, "su").Select(s => s.Label));
            Assert.Single(Language.Filter(items, "su", limit: 1));
            Assert.Empty(Language.Filter(items, ""));
        }

        [Fact]
        public void FilterWithSigilOffersOnlyThatKind()
        {
            Suggestion[] items = [new("$value", SuggestionKind.Variable, ""), new("VALUE", SuggestionKind.Function, "")];
            Assert.Equal(["$value"], Language.Filter(items, "$va").Select(s => s.Label));
        }

        [Fact]
        public void SuggestionsContainFunctionsConstantsAndKeywords()
        {
            var suggestions = Language.SuggestionsOf(Engine.ReferenceEnvironment, DefaultContext);
            Assert.Contains(suggestions, s => s is { Label: "ROUND", Kind: SuggestionKind.Function });
            Assert.Contains(suggestions, s => s is { Label: "PI", Kind: SuggestionKind.Constant });
            Assert.Contains(suggestions, s => s is { Label: "null", Kind: SuggestionKind.Keyword });
        }
    }
}
