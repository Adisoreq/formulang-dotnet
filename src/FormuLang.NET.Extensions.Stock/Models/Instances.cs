using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Model;
using FormuLang.NET.Core.Primitives;

namespace FormuLang.NET.Extensions.Stock.Models
{
    /// <summary>
    /// An instance (docs/implementation/formula_models.md, "Instance"): a definition with its normalized parameter
    /// values, which can be run over a chart. The host application keeps its own settings (e.g. colors, markets, exits)
    /// next to it.
    /// </summary>
    public abstract class Instance
    {
        /// <summary>The definition.</summary>
        public Definition Definition { get; }

        /// <summary>The normalized <c>PARAMETERS</c> Object.</summary>
        public IObject Parameters { get; }

        /// <summary>
        /// The identifier of the definition, <c>|</c> and the parameter key, e.g. <c>RSI|{ period: 14 }</c>.
        /// </summary>
        public string Key { get; }

        private protected Instance(Definition definition, IObject parameters)
        {
            Definition = definition;
            Parameters = parameters;
            Key = $"{definition.Id}|{Core.Model.Parameters.Key(parameters)}";
        }

        /// <summary>
        /// The parameters as a <c>PARAMETERS</c> value (a copy).
        /// </summary>
        public IValue ParametersValue => Value.Object(Copy(Parameters), "PARAMETERS");

        /// <summary>
        /// The value of a parameter, or <c>null</c> for a key that is not a parameter.
        /// </summary>
        public IValue? this[string key] => Parameters.HasKey(key) ? Parameters.GetProperty(key).GetValue() : null;

        /// <summary>
        /// The serialized form <c>{ id, parameters }</c>, with the parameters as .NET values.
        /// </summary>
        public InstanceJson ToJson()
            => new(Definition.Id, Parameters.Keys.ToDictionary(key => key, key => ValueConversion.ToClr(Parameters.GetProperty(key).GetValue()), StringComparer.Ordinal));

        public override string ToString() => Key;

        internal static Core.Primitives.Object Copy(IObject obj)
        {
            var result = new Core.Primitives.Object();

            foreach (string key in obj.Keys)
                result.InitVariable(key, obj.GetProperty(key).GetValue());

            return result;
        }
    }

    /// <summary>
    /// An instance of an indicator.
    /// </summary>
    public sealed class IndicatorInstance : Instance
    {
        public new IndicatorDefinition Definition => (IndicatorDefinition)base.Definition;

        internal IndicatorInstance(IndicatorDefinition definition, IObject parameters) : base(definition, parameters) { }

        /// <summary>
        /// Runs the indicator over the chart: one value of every output for every candle.
        /// </summary>
        public IndicatorSeries Compute(IEnvironment environment, Chart chart) => Live(environment, chart).Start();

        /// <summary>
        /// A live run over the chart; call <see cref="LiveRun{TResult}.Start"/>, then
        /// <see cref="LiveIndicatorRun.Append(IEnumerable{Candle}, IEnumerable{double?}?)"/> for the candles that close later.
        /// </summary>
        public LiveIndicatorRun Live(IEnvironment environment, Chart chart)
            => new(Definition.Formula, Parameters, Definition.Outputs?.Select(o => o.Key).ToArray(), environment, chart);
    }

    /// <summary>
    /// An instance of a candle pattern.
    /// </summary>
    public sealed class PatternInstance : Instance
    {
        public new PatternDefinition Definition => (PatternDefinition)base.Definition;

        internal PatternInstance(PatternDefinition definition, IObject parameters) : base(definition, parameters) { }

        /// <summary>
        /// Checks the pattern at every candle of the chart.
        /// </summary>
        public PatternSeries Compute(IEnvironment environment, Chart chart) => Live(environment, chart).Start();

        /// <summary>
        /// A live run over the chart.
        /// </summary>
        public LivePatternRun Live(IEnvironment environment, Chart chart) => new(Definition.Formula, Parameters, environment, chart);
    }

    /// <summary>
    /// An instance of a strategy.
    /// </summary>
    public sealed class StrategyInstance : Instance
    {
        public new StrategyDefinition Definition => (StrategyDefinition)base.Definition;

        internal StrategyInstance(StrategyDefinition definition, IObject parameters) : base(definition, parameters) { }

        /// <summary>
        /// Runs the strategy over the chart: its signals.
        /// </summary>
        public StrategySignals Compute(IEnvironment environment, Chart chart) => Live(environment, chart).Start();

        /// <summary>
        /// A live run over the chart.
        /// </summary>
        public LiveStrategyRun Live(IEnvironment environment, Chart chart) => new(Definition.Formula, Parameters, environment, chart);
    }
}
