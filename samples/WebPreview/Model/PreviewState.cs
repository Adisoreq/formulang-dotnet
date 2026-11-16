namespace WebPreview.Model
{
    /// <summary>
    /// State of the preview. Components change it directly and call <see cref="Notify"/>; every component re-renders
    /// and the formulas are executed again when something that they depend on changed.
    /// </summary>
    public sealed class PreviewState
    {
        public ContextKind Context { get; set; } = ContextKind.Default;

        /// <summary>Selected example of every context type.</summary>
        public Dictionary<ContextKind, string> Selected { get; } = [];

        /// <summary>Formulas of the examples, as edited (<c>kind:id</c>).</summary>
        public Dictionary<string, string> Formulas { get; } = [];

        /// <summary>Indicators as edited in the preview: definitions, parameter values and series styles.</summary>
        public List<IndicatorDraft> Indicators { get; } = Examples.Indicators.Select(IndicatorDraft.Of).ToList();

        /// <summary>Patterns as edited in the preview: definitions and appearance.</summary>
        public List<PatternDraft> Patterns { get; } = Examples.Patterns.Select(PatternDraft.Of).ToList();

        /// <summary>Strategies as edited in the preview: definitions, appearance and the values of the proposal.</summary>
        // The empty strategy is inactive until it has a formula
        public List<StrategyDraft> Strategies { get; } = Examples.Strategies.Select(s => StrategyDraft.Of(s, s.Id != Examples.NewFormula)).ToList();

        public int IterationCount { get; set; } = 8;

        public int CandleCount { get; private set; } = 160;

        public int Seed { get; private set; } = 20251;

        /// <summary>The candles of the chart; a new list for every generation, so the chart can compare it by reference.</summary>
        public IReadOnlyList<ChartCandle> Candles { get; private set; }

        public event Action? Changed;

        public PreviewState()
        {
            foreach (var (kind, examples) in Examples.ByContext)
            {
                Selected[kind] = examples[0].Id;
                foreach (Example example in examples)
                    Formulas[Examples.FormulaKey(kind, example.Id)] = Normalize(example.Formula);
            }

            Candles = Model.Candles.Generate(CandleCount, Seed);
        }

        /// <summary>The formulas are written with the new lines of the source file; the preview uses <c>\n</c>.</summary>
        private static string Normalize(string formula) => formula.ReplaceLineEndings("\n");

        /// <summary>Tells every component that the state changed.</summary>
        public void Notify() => Changed?.Invoke();

        public ContextDefinition ContextDefinition => Examples.Contexts.First(c => c.Kind == Context);

        /// <summary>Generates new mock candles; the chart is rebuilt.</summary>
        public void RegenerateCandles(int? count = null)
        {
            Seed = Model.Candles.RandomSeed();
            CandleCount = count ?? CandleCount;
            Candles = Model.Candles.Generate(CandleCount, Seed);
            Notify();
        }

        public string FormulaOf(ContextKind kind, string id) => Formulas.GetValueOrDefault(Examples.FormulaKey(kind, id)) ?? "";

        public string SelectedFormula(ContextKind? kind = null)
        {
            ContextKind k = kind ?? Context;
            return FormulaOf(k, Selected[k]);
        }

        public void SetFormula(string value, ContextKind? kind = null)
        {
            ContextKind k = kind ?? Context;
            Formulas[Examples.FormulaKey(k, Selected[k])] = value;
            Notify();
        }

        /// <summary>Restores the formula of the selected example.</summary>
        public void ResetFormula()
        {
            Example? example = Examples.ByContext[Context].FirstOrDefault(e => e.Id == Selected[Context]);
            if (example != null)
                SetFormula(Normalize(example.Formula));
        }

        public void Select(ContextKind kind, string id)
        {
            Selected[kind] = id;
            Notify();
        }

        public IndicatorDraft SelectedIndicator => Indicators.First(i => i.Id == Selected[ContextKind.Indicator]);

        public PatternDraft SelectedPattern => Patterns.First(p => p.Id == Selected[ContextKind.Pattern]);

        public StrategyDraft SelectedStrategy => Strategies.First(s => s.Id == Selected[ContextKind.Strategy]);

        /// <summary>Restores the definition of an indicator from its example (its formula stays).</summary>
        public void ResetIndicator(string id)
        {
            int index = Indicators.FindIndex(i => i.Id == id);
            IndicatorConfig? config = Examples.Indicators.FirstOrDefault(i => i.Id == id);
            if (index >= 0 && config != null)
                Indicators[index] = IndicatorDraft.Of(config);
            Notify();
        }

        /// <summary>Restores the definition and appearance of a pattern from its example (its formula stays).</summary>
        public void ResetPattern(string id)
        {
            int index = Patterns.FindIndex(p => p.Id == id);
            PatternConfig? config = Examples.Patterns.FirstOrDefault(p => p.Id == id);
            if (index >= 0 && config != null)
                Patterns[index] = PatternDraft.Of(config);
            Notify();
        }

        /// <summary>Restores the definition and appearance of a strategy from its example (its formula and activity stay).</summary>
        public void ResetStrategy(string id)
        {
            int index = Strategies.FindIndex(s => s.Id == id);
            StrategyConfig? config = Examples.Strategies.FirstOrDefault(s => s.Id == id);
            if (index >= 0 && config != null)
                Strategies[index] = StrategyDraft.Of(config, Strategies[index].Runtime.Active);
            Notify();
        }
    }
}
