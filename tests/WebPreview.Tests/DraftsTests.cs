namespace WebPreview.Tests
{
    /// <summary>Validation of the definitions edited in the preview.</summary>
    public class DraftsTests
    {
        private static IndicatorDraft Sma() => IndicatorDraft.Of(Examples.Indicators.First(i => i.Id == "sma"));

        [Fact]
        public void EveryExampleIndicatorIsValid()
        {
            var drafts = Examples.Indicators.Select(IndicatorDraft.Of).ToList();
            Assert.All(drafts, d => Assert.Empty(d.Problems(drafts)));
        }

        [Fact]
        public void InvalidRegistryIdIsReported()
        {
            IndicatorDraft draft = Sma();
            draft.IndicatorId = "1SMA";
            Assert.Contains(draft.Problems([draft]), p => p.Contains("not a valid identifier"));
        }

        [Fact]
        public void RegistryIdUsedTwiceIsReported()
        {
            IndicatorDraft a = Sma();
            IndicatorDraft b = IndicatorDraft.Of(Examples.Indicators.First(i => i.Id == "ema"));
            b.IndicatorId = a.IndicatorId;
            Assert.Contains(a.Problems([a, b]), p => p.Contains("used by another indicator"));
        }

        [Fact]
        public void IndicatorWithoutSeriesIsReported()
        {
            IndicatorDraft draft = Sma();
            draft.Series.Clear();
            Assert.Contains("The indicator has no series to draw.", draft.Problems([draft]));
        }

        [Fact]
        public void NewParametersAndSeriesGetFreeIds()
        {
            IndicatorDraft draft = Sma();
            draft.Parameters.Add(draft.NewParameter());
            draft.Parameters.Add(draft.NewParameter());
            draft.Series.Add(draft.NewSeries());

            Assert.Equal(draft.Parameters.Count, draft.Parameters.Select(p => p.Id).Distinct().Count());
            Assert.Equal(draft.Series.Count, draft.Series.Select(s => s.Id).Distinct().Count());
            Assert.Empty(draft.Problems([draft]));
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(2.5, false)]
        [InlineData(PatternDraft.MaxLength, true)]
        [InlineData(PatternDraft.MaxLength + 1, false)]
        public void PatternLengthMustBeWholeNumberInRange(double length, bool valid)
        {
            PatternDraft draft = PatternDraft.Of(Examples.Patterns[0]);
            draft.Length = length;
            Assert.Equal(valid, draft.Problems().Count == 0);
        }

        [Fact]
        public void EveryExamplePatternIsValid()
        {
            var drafts = Examples.Patterns.Select(PatternDraft.Of).ToList();
            Assert.All(drafts, d => Assert.Empty(d.Problems(drafts)));
        }

        [Fact]
        public void PatternRegistryIdMustBeUniqueAndValid()
        {
            var drafts = Examples.Patterns.Select(PatternDraft.Of).ToList();
            drafts[1].PatternId = drafts[0].PatternId;
            Assert.Contains(drafts[1].Problems(drafts), p => p.Contains("is used by another pattern"));

            drafts[1].PatternId = "with space";
            Assert.Contains(drafts[1].Problems(drafts), p => p.Contains("is not a valid identifier"));
        }

        [Fact]
        public void PatternParametersAreChecked()
        {
            PatternDraft draft = PatternDraft.Of(Examples.Patterns.First(p => p.Id == "hammer"));
            draft.Parameters.Add(draft.NewParameter());
            draft.Parameters[^1].Id = draft.Parameters[0].Id;

            Assert.Contains(draft.Problems(), p => p.Contains("is defined more than once"));
        }

        [Fact]
        public void StrategyWithBlankNameAndNegativeConfidenceHasTwoProblems()
        {
            StrategyDraft draft = StrategyDraft.Of(Examples.Strategies[0]);
            draft.Name = "   ";
            draft.Confidence = -1;
            Assert.Equal(2, draft.Problems().Count);
        }

        [Theory]
        [InlineData("Moving average crossover", "MAC")]
        [InlineData("momentum", "M")]
        [InlineData("", "")]
        public void InitialsOfName(string name, string initials)
            => Assert.Equal(initials, StrategyDraft.Initials(name));
    }
}
