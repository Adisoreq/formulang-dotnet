namespace FormuLang.NET.Conformance.Tests
{
    /// <summary>
    /// Every case of the conformance tests of the standard (<c>conformance/</c>, copied from
    /// <c>Adisoreq/FormuLang/tests</c>; the revision is in <c>conformance/REVISION</c>).
    /// </summary>
    public class ConformanceTests
    {
        /// <summary>
        /// Cases that disagree with the algorithm of the standard itself (docs/conformance.md, "Known deviations").
        /// They must keep failing: when the standard is fixed and the case passes, remove it from this list.
        /// </summary>
        public static IReadOnlyDictionary<string, string> KnownDeviations { get; } = new Dictionary<string, string>
        {
        };

        public static TheoryData<string> CaseIds()
        {
            var data = new TheoryData<string>();
            foreach (string id in ConformanceRunner.Cases.Keys)
                data.Add(id);
            return data;
        }

        [Theory]
        [MemberData(nameof(CaseIds))]
        public void Case(string id)
        {
            ConformanceCase testCase = ConformanceRunner.Cases[id];

            // A case that requires a standard the library does not implement is skipped (it passes trivially)
            if (testCase.Standards.Any(s => !ConformanceRunner.SupportedStandards.Contains(s)))
                return;

            IReadOnlyList<string> problems = ConformanceRunner.Run(testCase);

            if (KnownDeviations.TryGetValue(id, out string? reason))
            {
                Assert.True(problems.Count > 0, $"{id} passes now: remove it from the known deviations ({reason}).");
                return;
            }

            Assert.True(problems.Count == 0, $"{id} ({testCase.File}):\n{string.Join("\n", problems)}\nFormula:\n{testCase.Formula}");
        }

        [Fact]
        public void EveryFileIsLoaded()
        {
            Assert.True(File.Exists(Path.Combine(ConformanceRunner.Directory, "REVISION")));
            Assert.Contains(ConformanceRunner.Cases.Values, c => c.File.StartsWith("core/", StringComparison.Ordinal));
            Assert.Contains(ConformanceRunner.Cases.Values, c => c.File.StartsWith("stock/", StringComparison.Ordinal));
        }
    }
}
