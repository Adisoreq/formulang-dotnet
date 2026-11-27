using System.Text.Json;
using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Processing;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Model;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Extensions.Stock.Registry;

namespace FormuLang.NET.Extensions.Stock.Models
{
    /// <summary>
    /// Kind of a definition: the context type it is written for (the <c>context</c> key of the metadata).
    /// </summary>
    public enum DefinitionKind
    {
        /// <summary><c>"Stock.Indicator"</c></summary>
        Indicator,
        /// <summary><c>"Stock.Pattern"</c></summary>
        Pattern,
        /// <summary><c>"Stock.Strategy"</c></summary>
        Strategy,
    }

    /// <summary>
    /// Why a formula is not a valid definition (docs/implementation/formula_models.md, "Definition").
    /// </summary>
    public enum InvalidDefinitionReason
    {
        /// <summary>The validation or the metadata reading fails (<c>ERROR</c>).</summary>
        Error,
        /// <summary>A known key of the metadata has a value of a wrong type (<c>METADATA_TYPE</c>).</summary>
        MetadataType,
        /// <summary><c>id</c> is missing or is not a valid identifier (<c>ID</c>).</summary>
        Id,
        /// <summary><c>context</c> is missing or is not an indicator, a pattern or a strategy (<c>CONTEXT</c>).</summary>
        Context,
    }

    /// <summary>
    /// A formula that is not a valid definition.
    /// </summary>
    public sealed class InvalidDefinitionException(InvalidDefinitionReason reason, string message, IError? error = null)
        : Exception(message)
    {
        public InvalidDefinitionReason Reason { get; } = reason;

        /// <summary>
        /// The code of the standard: <c>ERROR</c>, <c>METADATA_TYPE</c>, <c>ID</c> or <c>CONTEXT</c>.
        /// </summary>
        public string ReasonCode => Reason switch
        {
            InvalidDefinitionReason.Error => "ERROR",
            InvalidDefinitionReason.MetadataType => "METADATA_TYPE",
            InvalidDefinitionReason.Id => "ID",
            _ => "CONTEXT",
        };

        /// <summary>
        /// The Error of the validation or of the metadata reading, for the reason <see cref="InvalidDefinitionReason.Error"/>.
        /// </summary>
        public IError? Error { get; } = error;
    }

    /// <summary>
    /// The parameter values that make an instance invalid.
    /// </summary>
    public sealed class ParameterException(IReadOnlyList<ParameterProblem> problems)
        : Exception(string.Join(" ", problems.Select(p => p.Message)))
    {
        public IReadOnlyList<ParameterProblem> Problems { get; } = problems;
    }

    /// <summary>
    /// An output of an indicator (the <c>stock.outputs</c> key of the metadata).
    /// </summary>
    /// <param name="Name">Name for the user; the key if it is not given.</param>
    public sealed record OutputDescription(string Key, string Name, string? Description);

    /// <summary>
    /// A condition of the <c>checks</c> of a strategy, with its label for the user (the <c>stock.checks</c> key).
    /// </summary>
    public sealed record CheckDescription(string Key, string Label);

    /// <summary>
    /// The serialized form of an instance: <c>{ "id": ..., "parameters": { ... } }</c>, with the parameters as .NET
    /// values (<see cref="ValueConversion.ToClr"/>), ready for <see cref="JsonSerializer"/>.
    /// </summary>
    public sealed record InstanceJson(string Id, IReadOnlyDictionary<string, object?> Parameters);

    /// <summary>
    /// A definition: a formula with the information read from its metadata (docs/implementation/formula_models.md,
    /// "Definition"). It does not depend on an environment.
    /// </summary>
    public abstract class Definition
    {
        private static readonly Dictionary<string, DefinitionKind> Kinds = new(StringComparer.Ordinal)
        {
            ["Stock.Indicator"] = DefinitionKind.Indicator,
            ["Stock.Pattern"] = DefinitionKind.Pattern,
            ["Stock.Strategy"] = DefinitionKind.Strategy,
        };

        public abstract DefinitionKind Kind { get; }

        /// <summary>Identifier of the formula (<c>id</c>).</summary>
        public string Id { get; }

        /// <summary>The formula text, with its metadata block.</summary>
        public string Formula { get; }

        /// <summary>The metadata Object of the formula.</summary>
        public IValue Metadata { get; }

        /// <summary>The parameter specifications, in the order of the keys of <c>parameters</c>.</summary>
        public IReadOnlyList<ParameterSpec> Parameters { get; }

        /// <summary>Name for the user; the identifier if <c>name</c> is not given.</summary>
        public string Name { get; }

        public string? Description { get; }

        /// <summary>A free group for the user (<c>stock.category</c>), or <c>null</c>.</summary>
        public string? Category { get; }

        private protected Definition(string id, string formula, IValue metadata, IReadOnlyList<ParameterSpec> parameters)
        {
            Id = id;
            Formula = formula;
            Metadata = metadata;
            Parameters = parameters;

            IObject? obj = metadata.Raw as IObject;
            Name = Text(obj, "name") ?? id;
            Description = Text(obj, "description");
            Category = Text(ObjectOf(obj, "stock"), "category");
        }

        /// <summary>
        /// Reads a definition from the metadata of a formula.
        /// </summary>
        /// <param name="environment">Environment of the validation (a new Core environment by default).</param>
        /// <exception cref="InvalidDefinitionException">The formula is not a valid definition.</exception>
        public static Definition FromFormula(string formula, IEnvironment? environment = null)
        {
            ArgumentNullException.ThrowIfNull(formula);

            ValidationResult validation = Validator.Validate(formula, environment ?? Core.Structures.Environment.Create());
            if (validation.Errors.Count > 0)
                throw new InvalidDefinitionException(InvalidDefinitionReason.Error, validation.Errors[0].Message, validation.Errors[0]);

            MetadataResult reading = MetadataReader.Read(formula);
            if (reading.Error.IsFailed())
                throw new InvalidDefinitionException(InvalidDefinitionReason.Error, reading.Error.Message, reading.Error);

            IValue metadata = reading.Metadata;
            List<IWarning> typeWarnings = Warnings.MetadataTypeWarnings(metadata, reading.Position ?? new SourcePosition(1, 1),
                key => key == "stock" ? StockStandard.MetadataChecker : null);

            if (typeWarnings.Count > 0)
                throw new InvalidDefinitionException(InvalidDefinitionReason.MetadataType, string.Join(" ", typeWarnings.Select(w => w.Message)));

            IObject obj = reading.Object;

            if (Text(obj, "id") is not string id || !FormulaIds.IsValid(id))
                throw new InvalidDefinitionException(InvalidDefinitionReason.Id, "The metadata has no valid \"id\".");

            if (Text(obj, "context") is not string context || !Kinds.TryGetValue(context, out DefinitionKind kind))
                throw new InvalidDefinitionException(InvalidDefinitionReason.Context,
                    "The \"context\" of the metadata must be \"Stock.Indicator\", \"Stock.Pattern\" or \"Stock.Strategy\".");

            IReadOnlyList<ParameterSpec> parameters = ParameterSpec.FromMetadata(metadata);

            return kind switch
            {
                DefinitionKind.Indicator => new IndicatorDefinition(id, formula, metadata, parameters),
                DefinitionKind.Pattern => new PatternDefinition(id, formula, metadata, parameters),
                _ => new StrategyDefinition(id, formula, metadata, parameters),
            };
        }

        /// <summary>
        /// Reads a definition from the metadata of a formula, or gives the reason why it is not a valid one.
        /// </summary>
        public static bool TryFromFormula(string formula, out Definition? definition, out InvalidDefinitionException? problem,
            IEnvironment? environment = null)
        {
            try
            {
                definition = FromFormula(formula, environment);
                problem = null;
                return true;
            }
            catch (InvalidDefinitionException exception)
            {
                definition = null;
                problem = exception;
                return false;
            }
        }

        // Parameter normalization

        /// <summary>
        /// The parameter normalization of the given values (docs/implementation/formula_models.md, "Parameter
        /// normalization"), e.g. to show the problems in an editor before an instance is created.
        /// </summary>
        public ParameterNormalization Normalize(IObject? given, IEnvironment? environment = null)
            => Core.Model.Parameters.Normalize(Parameters, given, environment);

        /// <inheritdoc cref="Normalize(IObject?, IEnvironment?)"/>
        public ParameterNormalization Normalize(IEnumerable<KeyValuePair<string, IValue>>? given, IEnvironment? environment = null)
            => Core.Model.Parameters.Normalize(Parameters, given, environment);

        /// <summary>
        /// The parameter normalization of values of .NET (numbers, texts, Booleans, lists, dictionaries,
        /// <see cref="JsonElement"/>s, <see cref="IValue"/>s); a value that cannot be converted is a <c>TYPE</c> problem.
        /// </summary>
        public ParameterNormalization Normalize(IEnumerable<KeyValuePair<string, object?>>? given, IEnvironment? environment = null)
            => Core.Model.Parameters.Normalize(Parameters, Convert(given), environment);

        /// <summary>
        /// The parameter normalization of a JSON Object of values; <c>null</c> or an undefined element is no value.
        /// </summary>
        public ParameterNormalization Normalize(JsonElement given, IEnvironment? environment = null)
            => Core.Model.Parameters.Normalize(Parameters, Convert(given), environment);

        // Instances

        /// <summary>
        /// An instance with the given parameter values; missing ones get their defaults.
        /// </summary>
        /// <exception cref="ParameterException">The values have problems.</exception>
        public Instance CreateInstance(IObject? given = null, IEnvironment? environment = null)
            => InstanceOf(Normalize(given, environment));

        /// <inheritdoc cref="CreateInstance(IObject?, IEnvironment?)"/>
        public Instance CreateInstance(IEnumerable<KeyValuePair<string, object?>>? given, IEnvironment? environment = null)
            => InstanceOf(Normalize(given, environment));

        /// <inheritdoc cref="CreateInstance(IObject?, IEnvironment?)"/>
        public Instance CreateInstance(JsonElement given, IEnvironment? environment = null)
            => InstanceOf(Normalize(given, environment));

        /// <summary>
        /// An instance read back from its serialized form; its parameters are normalized again.
        /// </summary>
        /// <exception cref="ArgumentException">The identifier is not the identifier of the definition.</exception>
        /// <exception cref="ParameterException">The values have problems.</exception>
        public Instance InstanceFromJson(InstanceJson json, IEnvironment? environment = null)
        {
            if (json.Id != Id)
                throw new ArgumentException($"The instance belongs to \"{json.Id}\", not to \"{Id}\".", nameof(json));

            return CreateInstance(json.Parameters, environment);
        }

        /// <inheritdoc cref="InstanceFromJson(InstanceJson, IEnvironment?)"/>
        public Instance InstanceFromJson(JsonElement json, IEnvironment? environment = null)
        {
            string? id = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("id", out JsonElement idElement) ? idElement.GetString() : null;

            if (id != Id)
                throw new ArgumentException($"The instance belongs to \"{id}\", not to \"{Id}\".", nameof(json));

            return CreateInstance(json.TryGetProperty("parameters", out JsonElement parameters) ? parameters : default, environment);
        }

        private Instance InstanceOf(ParameterNormalization normalization)
            => normalization.Parameters is IObject parameters ? CreateInstanceCore(parameters) : throw new ParameterException(normalization.Problems);

        private protected abstract Instance CreateInstanceCore(IObject parameters);

        /// <summary>
        /// The definition of the formula registry, for <c>LOAD_INDICATOR</c> or <c>LOAD_PATTERN</c>: the identifier, the
        /// formula and the defaults of the parameters (<c>null</c> for a parameter without a default).
        /// </summary>
        /// <exception cref="InvalidOperationException">A strategy, which is not a part of the formula registry.</exception>
        public FormulaDefinition ToRegistryDefinition()
        {
            if (Kind == DefinitionKind.Strategy)
                throw new InvalidOperationException("Only indicators and patterns are a part of the formula registry.");

            return new FormulaDefinition(Id, Formula, Parameters.Select(spec => new FormulaParameter(spec.Key, spec.Default ?? Value.Null("null"))));
        }

        public override string ToString() => $"{Kind} \"{Id}\"";

        // Helpers

        internal static string? Text(IObject? obj, string key)
            => obj != null && obj.HasKey(key) && obj.GetProperty(key).GetValue() is { Raw: string text } value && value.Type.Base == Types.TextName ? text : null;

        internal static IObject? ObjectOf(IObject? obj, string key)
            => obj != null && obj.HasKey(key) && obj.GetProperty(key).GetValue() is { Raw: IObject inner } value && value.Type.Base == Types.ObjectName ? inner : null;

        private static IEnumerable<KeyValuePair<string, IValue>>? Convert(IEnumerable<KeyValuePair<string, object?>>? given)
            => given?.Select(pair => new KeyValuePair<string, IValue>(pair.Key,
                ValueConversion.TryFromClr(pair.Value, out IValue value) ? value : Value.Undefined(pair.Key))).ToArray();

        private static IEnumerable<KeyValuePair<string, IValue>>? Convert(JsonElement given)
        {
            if (given.ValueKind != JsonValueKind.Object)
                return null;

            return given.EnumerateObject()
                .Where(property => property.Value.ValueKind != JsonValueKind.Undefined)
                .Select(property => new KeyValuePair<string, IValue>(property.Name,
                    ValueConversion.TryFromJson(property.Value, out IValue value) ? value : Value.Undefined(property.Name)))
                .ToArray();
        }
    }

    /// <summary>
    /// An indicator: a formula of the Indicator Context.
    /// </summary>
    public sealed class IndicatorDefinition : Definition
    {
        public override DefinitionKind Kind => DefinitionKind.Indicator;

        /// <summary>
        /// The outputs of the metadata, in their order; <c>null</c> if the metadata does not give them (the outputs are
        /// then the keys of the results).
        /// </summary>
        public IReadOnlyList<OutputDescription>? Outputs { get; }

        internal IndicatorDefinition(string id, string formula, IValue metadata, IReadOnlyList<ParameterSpec> parameters)
            : base(id, formula, metadata, parameters)
        {
            IObject? outputs = ObjectOf(ObjectOf(metadata.Raw as IObject, "stock"), "outputs");

            Outputs = outputs?.Keys.Select(key =>
            {
                IObject? description = ObjectOf(outputs, key);
                return new OutputDescription(key, Text(description, "name") ?? key, Text(description, "description"));
            }).ToArray();
        }

        /// <inheritdoc cref="Definition.CreateInstance(IObject?, IEnvironment?)"/>
        public new IndicatorInstance CreateInstance(IObject? given = null, IEnvironment? environment = null)
            => (IndicatorInstance)base.CreateInstance(given, environment);

        /// <inheritdoc cref="Definition.CreateInstance(IObject?, IEnvironment?)"/>
        public new IndicatorInstance CreateInstance(IEnumerable<KeyValuePair<string, object?>>? given, IEnvironment? environment = null)
            => (IndicatorInstance)base.CreateInstance(given, environment);

        /// <inheritdoc cref="Definition.CreateInstance(IObject?, IEnvironment?)"/>
        public new IndicatorInstance CreateInstance(JsonElement given, IEnvironment? environment = null)
            => (IndicatorInstance)base.CreateInstance(given, environment);

        private protected override Instance CreateInstanceCore(IObject parameters) => new IndicatorInstance(this, parameters);
    }

    /// <summary>
    /// A candle pattern: a formula of the Pattern Context.
    /// </summary>
    public sealed class PatternDefinition : Definition
    {
        public override DefinitionKind Kind => DefinitionKind.Pattern;

        internal PatternDefinition(string id, string formula, IValue metadata, IReadOnlyList<ParameterSpec> parameters)
            : base(id, formula, metadata, parameters) { }

        /// <inheritdoc cref="Definition.CreateInstance(IObject?, IEnvironment?)"/>
        public new PatternInstance CreateInstance(IObject? given = null, IEnvironment? environment = null)
            => (PatternInstance)base.CreateInstance(given, environment);

        /// <inheritdoc cref="Definition.CreateInstance(IObject?, IEnvironment?)"/>
        public new PatternInstance CreateInstance(IEnumerable<KeyValuePair<string, object?>>? given, IEnvironment? environment = null)
            => (PatternInstance)base.CreateInstance(given, environment);

        /// <inheritdoc cref="Definition.CreateInstance(IObject?, IEnvironment?)"/>
        public new PatternInstance CreateInstance(JsonElement given, IEnvironment? environment = null)
            => (PatternInstance)base.CreateInstance(given, environment);

        private protected override Instance CreateInstanceCore(IObject parameters) => new PatternInstance(this, parameters);
    }

    /// <summary>
    /// A strategy: a formula of the Strategy Context.
    /// </summary>
    public sealed class StrategyDefinition : Definition
    {
        public override DefinitionKind Kind => DefinitionKind.Strategy;

        /// <summary>
        /// The labels of the <c>checks</c> of the signal object, from the metadata; <c>null</c> if it does not give them.
        /// </summary>
        public IReadOnlyList<CheckDescription>? Checks { get; }

        internal StrategyDefinition(string id, string formula, IValue metadata, IReadOnlyList<ParameterSpec> parameters)
            : base(id, formula, metadata, parameters)
        {
            IObject? checks = ObjectOf(ObjectOf(metadata.Raw as IObject, "stock"), "checks");
            Checks = checks?.Keys.Select(key => new CheckDescription(key, Text(checks, key) ?? key)).ToArray();
        }

        /// <inheritdoc cref="Definition.CreateInstance(IObject?, IEnvironment?)"/>
        public new StrategyInstance CreateInstance(IObject? given = null, IEnvironment? environment = null)
            => (StrategyInstance)base.CreateInstance(given, environment);

        /// <inheritdoc cref="Definition.CreateInstance(IObject?, IEnvironment?)"/>
        public new StrategyInstance CreateInstance(IEnumerable<KeyValuePair<string, object?>>? given, IEnvironment? environment = null)
            => (StrategyInstance)base.CreateInstance(given, environment);

        /// <inheritdoc cref="Definition.CreateInstance(IObject?, IEnvironment?)"/>
        public new StrategyInstance CreateInstance(JsonElement given, IEnvironment? environment = null)
            => (StrategyInstance)base.CreateInstance(given, environment);

        private protected override Instance CreateInstanceCore(IObject parameters) => new StrategyInstance(this, parameters);
    }
}
