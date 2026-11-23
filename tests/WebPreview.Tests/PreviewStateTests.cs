namespace WebPreview.Tests
{
    /// <summary>State of the preview: selection, edited formulas and resets.</summary>
    public class PreviewStateTests
    {
        [Fact]
        public void FirstExampleOfEveryContextIsSelected()
        {
            var state = new PreviewState();
            foreach (var (kind, examples) in Examples.ByContext)
                Assert.Equal(examples[0].Id, state.Selected[kind]);
        }

        [Fact]
        public void FormulasUseUnixNewLines()
        {
            var state = new PreviewState();
            Assert.All(state.Formulas.Values, f => Assert.DoesNotContain('\r', f));
        }

        [Fact]
        public void SetFormulaNotifiesAndResetRestoresIt()
        {
            var state = new PreviewState();
            string original = state.SelectedFormula();
            int notifications = 0;
            state.Changed += () => notifications++;

            state.SetFormula("1 + 1");
            Assert.Equal("1 + 1", state.SelectedFormula());

            state.ResetFormula();
            Assert.Equal(original, state.SelectedFormula());
            Assert.Equal(2, notifications);
        }

        [Fact]
        public void FormulasOfContextsAreIndependent()
        {
            var state = new PreviewState();
            string iteration = state.SelectedFormula(ContextKind.Iteration);

            state.SetFormula("42", ContextKind.Default);

            Assert.Equal(iteration, state.SelectedFormula(ContextKind.Iteration));
        }

        [Fact]
        public void ResetIndicatorRestoresDefinition()
        {
            var state = new PreviewState();
            state.SelectedIndicator.Name = "Changed";

            state.ResetIndicator(state.SelectedIndicator.Id);

            Assert.Equal(Examples.Indicators[0].Name, state.SelectedIndicator.Name);
        }

        [Fact]
        public void ResetStrategyKeepsActivity()
        {
            var state = new PreviewState();
            StrategyDraft strategy = state.SelectedStrategy;
            strategy.Runtime.Active = false;
            strategy.Label = "X";

            state.ResetStrategy(strategy.Id);

            Assert.False(state.SelectedStrategy.Runtime.Active);
            Assert.NotEqual("X", state.SelectedStrategy.Label);
        }

        [Fact]
        public void EmptyStrategyIsInactive()
        {
            var state = new PreviewState();
            Assert.False(state.Strategies.Single(s => s.Id == Examples.NewFormula).Runtime.Active);
        }

        [Fact]
        public void RegenerateCandlesChangesCountAndList()
        {
            var state = new PreviewState();
            var before = state.Candles;

            state.RegenerateCandles(30);

            Assert.Equal(30, state.CandleCount);
            Assert.Equal(30, state.Candles.Count);
            Assert.NotSame(before, state.Candles);
        }
    }
}
