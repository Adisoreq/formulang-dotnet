using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Utils;
using FormuLang.NET.Core.Validation;

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
            if (!IdentifierValidator.Validate(id))
                throw new ArgumentException($"\"{id}\" is not a valid identifier.", nameof(id));

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
    }
}
