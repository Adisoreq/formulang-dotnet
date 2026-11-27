using System.Text.RegularExpressions;
using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Core.Utils;
using FormuLang.NET.Core.Validation;
using FormuLang.NET.Extensions.Stock.Contexts;

namespace FormuLang.NET.Extensions.Stock.Registry
{
    /// <summary>
    /// Parameter of an indicator or a pattern with its default value, e.g. <c>period</c> = <c>14</c>.
    /// </summary>
    public sealed record FormulaParameter(string Name, IValue DefaultValue)
    {
        public FormulaParameter(string name, double defaultValue)
            : this(name, Value.Number(defaultValue, StringConverter.NumberToString(defaultValue))) { }
    }

    /// <summary>
    /// Identifiers of the registry definitions and of the <c>id</c> of the metadata.
    /// </summary>
    public static partial class FormulaIds
    {
        [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
        private static partial Regex Pattern();

        /// <summary>
        /// Whether a text is a valid identifier of a registry definition or of the <c>id</c> of the metadata: letters,
        /// digits, <c>_</c> and <c>-</c>, starting with a letter or <c>_</c> (e.g. <c>"macd-ema200"</c>). It is always a
        /// Text, never an identifier token, so it may contain <c>-</c> (since <c>stock-1.2</c>).
        /// </summary>
        public static bool IsValid(string id) => Pattern().IsMatch(id);
    }

    /// <summary>
    /// Definition of the formula registry (docs/standards/stock/README.md, "Formula registry"): an identifier, a formula
    /// and parameters with their default values.
    /// </summary>
    public sealed class FormulaDefinition
    {
        /// <summary>
        /// Identifier used to load the definition, e.g. <c>"SMA"</c> or <c>"ENGULFING"</c>.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Formula executed once for every candle, in the context of its part of the registry.
        /// </summary>
        public string Formula { get; }

        /// <summary>
        /// Parameters with their default values, in the order of the definition. May be empty.
        /// </summary>
        public IReadOnlyList<FormulaParameter> Parameters { get; }

        /// <exception cref="ArgumentException">
        /// The identifier or a parameter name is not a valid identifier, or a parameter is defined twice.
        /// </exception>
        public FormulaDefinition(string id, string formula, IEnumerable<FormulaParameter> parameters)
        {
            if (!FormulaIds.IsValid(id))
                throw new ArgumentException($"\"{id}\" is not a valid identifier of a definition.", nameof(id));

            Parameters = parameters.ToArray();

            foreach (FormulaParameter parameter in Parameters)
            {
                if (!IdentifierValidator.Validate(parameter.Name))
                    throw new ArgumentException($"\"{parameter.Name}\" is not a valid parameter name.", nameof(parameters));

                if (parameter.DefaultValue.Type.Base is Types.ErrorName or Types.UndefinedName)
                    throw new ArgumentException($"Parameter \"{parameter.Name}\" has no valid default value.", nameof(parameters));
            }

            if (Parameters.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != Parameters.Count)
                throw new ArgumentException("A parameter is defined more than once.", nameof(parameters));

            Id = id;
            Formula = formula;
        }

        public FormulaDefinition(string id, string formula, params FormulaParameter[] parameters)
            : this(id, formula, (IEnumerable<FormulaParameter>)parameters) { }

        public FormulaParameter? GetParameter(string name) => Parameters.FirstOrDefault(p => p.Name == name);

        /// <summary>
        /// A definition read from the metadata of its formula (docs/standards/stock/README.md, "Formula registry", since
        /// <c>stock-1.2</c>): the identifier is <c>id</c>, the parameters are the keys of <c>parameters</c> with their
        /// <c>default</c> values (<c>null</c> without a default).
        /// </summary>
        /// <exception cref="ArgumentException">The metadata block is not valid or has no valid <c>id</c>.</exception>
        public static FormulaDefinition FromFormula(string formula)
        {
            var (id, _, parameters) = ReadDefinition(formula);
            return new FormulaDefinition(id, formula, parameters);
        }

        /// <summary>
        /// The identifier, the context and the parameters of the metadata of a formula.
        /// </summary>
        internal static (string Id, string? Context, FormulaParameter[] Parameters) ReadDefinition(string formula)
        {
            MetadataResult reading = MetadataReader.Read(formula);

            if (reading.Error.IsFailed())
                throw new ArgumentException(reading.Error.Message, nameof(formula));

            IObject metadata = reading.Object;

            if (!metadata.HasKey("id") || metadata.GetProperty("id").GetValue() is not { Raw: string id })
                throw new ArgumentException("The metadata of the formula has no Text \"id\".", nameof(formula));

            string? context = metadata.HasKey("context") ? metadata.GetProperty("context").GetValue().Raw as string : null;
            FormulaParameter[] parameters = StrategyContext.ParameterDefaults(reading.Metadata)
                .Select(p => new FormulaParameter(p.Key, p.Value ?? Value.Null("null")))
                .ToArray();

            return (id, context, parameters);
        }
    }

    /// <summary>
    /// One part of the formula registry: the indicators or the patterns. Identifiers are unique within a part.
    /// </summary>
    public sealed class FormulaCollection
    {
        private readonly Dictionary<string, FormulaDefinition> _definitions = new(StringComparer.Ordinal);

        /// <summary>
        /// Name of the part, e.g. <c>Indicator</c>; used in the error messages.
        /// </summary>
        public string Kind { get; }

        internal FormulaCollection(string kind)
        {
            Kind = kind;
        }

        public IEnumerable<FormulaDefinition> Definitions => _definitions.Values;

        public int Count => _definitions.Count;

        /// <exception cref="InvalidOperationException">A definition with the same identifier is already registered.</exception>
        public FormulaCollection Add(FormulaDefinition definition)
        {
            if (!_definitions.TryAdd(definition.Id, definition))
                throw new InvalidOperationException($"{Kind} \"{definition.Id}\" is already registered.");

            return this;
        }

        /// <summary>
        /// Registers a definition, e.g. <c>Add("SMA", formula, new FormulaParameter("period", 14))</c>.
        /// </summary>
        public FormulaCollection Add(string id, string formula, params FormulaParameter[] parameters)
            => Add(new FormulaDefinition(id, formula, parameters));

        public bool Remove(string id) => _definitions.Remove(id);

        public void Clear() => _definitions.Clear();

        public bool Contains(string id) => _definitions.ContainsKey(id);

        public FormulaDefinition? Get(string id) => _definitions.GetValueOrDefault(id);
    }

    /// <summary>
    /// Formula registry of the environment (docs/standards/stock/README.md, "Formula registry"), provided by the host
    /// application. Formulas use it only through <c>LOAD_INDICATOR</c> and <c>LOAD_PATTERN</c>. An indicator and a
    /// pattern may have the same identifier.
    /// </summary>
    public sealed class FormulaRegistry
    {
        /// <summary>
        /// Indicators, executed in an Indicator Context and loaded with <c>LOAD_INDICATOR</c>.
        /// </summary>
        public FormulaCollection Indicators { get; } = new("Indicator");

        /// <summary>
        /// Patterns, executed in a Pattern Context and loaded with <c>LOAD_PATTERN</c>.
        /// </summary>
        public FormulaCollection Patterns { get; } = new("Pattern");

        /// <summary>
        /// Adds a definition read from the metadata of its formula (since <c>stock-1.2</c>): to the indicators for the
        /// <c>context</c> <c>"Stock.Indicator"</c>, to the patterns for <c>"Stock.Pattern"</c>.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The metadata is not valid, has no <c>id</c>, or its <c>context</c> is not an indicator or a pattern.
        /// </exception>
        /// <exception cref="InvalidOperationException">A definition with the identifier is already registered in the part.</exception>
        public FormulaDefinition AddFormula(string formula)
        {
            var (id, context, parameters) = FormulaDefinition.ReadDefinition(formula);

            FormulaCollection part = context switch
            {
                "Stock.Indicator" => Indicators,
                "Stock.Pattern" => Patterns,
                _ => throw new ArgumentException("The \"context\" of the metadata must be \"Stock.Indicator\" or \"Stock.Pattern\".", nameof(formula)),
            };

            var definition = new FormulaDefinition(id, formula, parameters);
            part.Add(definition);
            return definition;
        }

        /// <summary>
        /// Adds the definition of an indicator or a pattern model (<see cref="Models.IndicatorDefinition"/>,
        /// <see cref="Models.PatternDefinition"/>) to its part of the registry.
        /// </summary>
        /// <exception cref="ArgumentException">A strategy, which is not a part of the formula registry.</exception>
        /// <exception cref="InvalidOperationException">A definition with the identifier is already registered in the part.</exception>
        public FormulaDefinition AddDefinition(Models.Definition model)
        {
            FormulaCollection part = model.Kind switch
            {
                Models.DefinitionKind.Indicator => Indicators,
                Models.DefinitionKind.Pattern => Patterns,
                _ => throw new ArgumentException("Only indicators and patterns are a part of the formula registry.", nameof(model)),
            };

            FormulaDefinition definition = model.ToRegistryDefinition();
            part.Add(definition);
            return definition;
        }
    }
}
