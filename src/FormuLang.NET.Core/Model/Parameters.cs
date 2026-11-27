using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Functions;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Standards;
using FormuLang.NET.Core.Utils;

namespace FormuLang.NET.Core.Model
{
    /// <summary>
    /// Codes of the problems of the parameter normalization (FormuLang docs/implementation/formula_models.md,
    /// "Parameter normalization").
    /// </summary>
    public enum ParameterProblemCode
    {
        /// <summary>A given key is not a parameter of the formula (<c>UNKNOWN</c>).</summary>
        Unknown,
        /// <summary>A parameter without a default is not given (<c>MISSING</c>).</summary>
        Missing,
        /// <summary>The value is not of the type of the parameter (<c>TYPE</c>).</summary>
        Type,
        /// <summary>The value is lower than the lowest allowed value (<c>MIN</c>).</summary>
        Min,
        /// <summary>The value is greater than the highest allowed value (<c>MAX</c>).</summary>
        Max,
        /// <summary>The value is not one of the allowed values (<c>OPTION</c>).</summary>
        Option,
    }

    /// <summary>
    /// A problem of the parameter normalization: a code, the key of the parameter and a message for the user.
    /// </summary>
    public sealed record ParameterProblem(ParameterProblemCode Code, string Key, string Message)
    {
        /// <summary>
        /// The code of the standard: <c>UNKNOWN</c>, <c>MISSING</c>, <c>TYPE</c>, <c>MIN</c>, <c>MAX</c> or <c>OPTION</c>.
        /// </summary>
        public string CodeName => Code.ToString().ToUpperInvariant();

        public override string ToString() => $"{CodeName} ({Key}): {Message}";
    }

    /// <summary>
    /// Specification of one parameter of a formula (docs/implementation/formula_models.md, "Parameter specification"),
    /// read from a parameter description of the <c>parameters</c> key of the metadata (docs/standards/core/README.md,
    /// "Metadata").
    /// </summary>
    public sealed class ParameterSpec
    {
        private static readonly HashSet<string> BaseTypes =
        [
            Types.NullName, Types.UndefinedName, Types.BooleanName, Types.NumberName, Types.TextName, Types.ErrorName,
            Types.ArrayName, Types.ObjectName, Types.DelegateName,
        ];

        /// <summary>Name of the parameter, the key of <c>PARAMETERS</c>.</summary>
        public string Key { get; }

        /// <summary>Base or extended type of the value, e.g. <c>"Number"</c> or <c>"Stock.Candle"</c>; <c>null</c> for any value.</summary>
        public string? Type { get; }

        /// <summary>Default value; <c>null</c> if there is none (a default <c>null</c> of the formula is a Null value).</summary>
        public IValue? Default { get; }

        /// <summary>Name for the user; the key if the description has no <c>name</c>.</summary>
        public string Name { get; }

        /// <summary>Description for the user, or <c>null</c>.</summary>
        public string? Description { get; }

        /// <summary>Lowest allowed value of a Number, or <c>null</c>.</summary>
        public double? Min { get; }

        /// <summary>Highest allowed value of a Number, or <c>null</c>.</summary>
        public double? Max { get; }

        /// <summary>Allowed values; <c>null</c> if any value of the type is allowed.</summary>
        public IReadOnlyList<IValue>? Options { get; }

        public ParameterSpec(string key, string? type = null, IValue? defaultValue = null, string? name = null,
            string? description = null, double? min = null, double? max = null, IReadOnlyList<IValue>? options = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            Key = key;
            Type = type;
            Default = defaultValue;
            Name = name ?? key;
            Description = description;
            Min = min;
            Max = max;
            Options = options?.ToArray();
        }

        /// <summary>
        /// The specifications of the <c>parameters</c> of a metadata Object, in the order of their keys; empty without
        /// <c>parameters</c>.
        /// </summary>
        /// <exception cref="FormatException">
        /// <c>parameters</c> is not an Object of parameter descriptions with values of the right types (the
        /// <c>METADATA_TYPE</c> warnings).
        /// </exception>
        public static IReadOnlyList<ParameterSpec> FromMetadata(IValue metadata)
        {
            if (metadata.Raw is not IObject obj || !obj.HasKey("parameters"))
                return [];

            if (obj.GetProperty("parameters").GetValue() is not { Raw: IObject parameters } || !IsBase(obj.GetProperty("parameters").GetValue(), Types.ObjectName))
                throw new FormatException("The metadata key \"parameters\" must be an Object.");

            return parameters.Keys.Select(key => FromDescription(key, parameters.GetProperty(key).GetValue())).ToArray();
        }

        private static ParameterSpec FromDescription(string key, IValue description)
        {
            if (description.Raw is not IObject obj || !IsBase(description, Types.ObjectName))
                throw new FormatException($"The description of the parameter \"{key}\" must be an Object.");

            IValue? Read(string name, string type)
            {
                if (!obj.HasKey(name))
                    return null;

                IValue value = obj.GetProperty(name).GetValue();
                if (!IsBase(value, type))
                    throw new FormatException($"Metadata key \"parameters.{key}.{name}\" must be of type {type}.");

                return value;
            }

            return new ParameterSpec(
                key,
                Read("type", Types.TextName)?.Raw as string,
                obj.HasKey("default") ? obj.GetProperty("default").GetValue() : null,
                Read("name", Types.TextName)?.Raw as string,
                Read("description", Types.TextName)?.Raw as string,
                Read("min", Types.NumberName)?.Raw as double?,
                Read("max", Types.NumberName)?.Raw as double?,
                Read("options", Types.ArrayName)?.Raw as IValue[]);
        }

        /// <summary>
        /// Whether a value is of the type of the parameter (an extended type also accepts the types derived from it, with
        /// the extended type tree of <paramref name="environment"/>).
        /// </summary>
        public bool Accepts(IValue value, IEnvironment? environment = null)
        {
            if (Type == null)
                return true;

            if (BaseTypes.Contains(Type))
                return value.Type.Base == Type;

            if (environment == null)
                return value.Type.Extended == Type;

            return ArgumentUtils.MatchTemplateType(value, ArgumentTemplate.Of(Key, Types.Of(Types.ObjectName, Type)), environment);
        }

        private static bool IsBase(IValue value, string type) => value.Type.Base == type;
    }

    /// <summary>
    /// Result of the parameter normalization: the normalized Object, or the problems (never both).
    /// </summary>
    public sealed class ParameterNormalization
    {
        /// <summary>The normalized <c>PARAMETERS</c> Object; <c>null</c> if there are problems.</summary>
        public IObject? Parameters { get; }

        public IReadOnlyList<ParameterProblem> Problems { get; }

        public bool Successful => Problems.Count == 0;

        internal ParameterNormalization(IObject? parameters, IReadOnlyList<ParameterProblem> problems)
        {
            Parameters = parameters;
            Problems = problems;
        }
    }

    /// <summary>
    /// Parameter normalization and the parameter key (docs/implementation/formula_models.md).
    /// </summary>
    public static class Parameters
    {
        /// <summary>
        /// Parameter normalization (docs/implementation/formula_models.md, "Parameter normalization"): the complete
        /// <c>PARAMETERS</c> Object of the given values and the defaults, in the order of the specifications, or every
        /// problem: unknown keys first, then the problems of the specifications in their order.
        /// </summary>
        /// <param name="given">The given values, in their order; <c>null</c> for none.</param>
        /// <param name="environment">The environment whose extended type tree is used for a parameter of an extended type.</param>
        public static ParameterNormalization Normalize(IReadOnlyList<ParameterSpec> specs,
            IEnumerable<KeyValuePair<string, IValue>>? given = null, IEnvironment? environment = null)
        {
            var problems = new List<ParameterProblem>();
            var values = new Dictionary<string, IValue>(StringComparer.Ordinal);
            var order = new List<string>();

            foreach (var (key, value) in given ?? [])
            {
                if (values.TryAdd(key, value))
                    order.Add(key);
                else
                    values[key] = value;
            }

            var keys = specs.Select(spec => spec.Key).ToHashSet(StringComparer.Ordinal);

            foreach (string key in order)
            {
                if (!keys.Contains(key))
                    problems.Add(new ParameterProblem(ParameterProblemCode.Unknown, key, $"\"{key}\" is not a parameter."));
            }

            var normalized = new Primitives.Object();

            foreach (ParameterSpec spec in specs)
            {
                IValue? value = values.GetValueOrDefault(spec.Key) ?? spec.Default;

                if (value == null)
                {
                    problems.Add(new ParameterProblem(ParameterProblemCode.Missing, spec.Key, $"The parameter \"{spec.Name}\" is required."));
                    continue;
                }

                // A given value that could not be converted to a FormuLang value (e.g. a .NET delegate) is Undefined
                if (value.Type.Base == Types.UndefinedName)
                {
                    problems.Add(new ParameterProblem(ParameterProblemCode.Type, spec.Key, $"The value of the parameter \"{spec.Name}\" is not a FormuLang value."));
                    continue;
                }

                if (!spec.Accepts(value, environment))
                {
                    problems.Add(new ParameterProblem(ParameterProblemCode.Type, spec.Key, $"The parameter \"{spec.Name}\" must be of type {spec.Type}."));
                    continue;
                }

                if (value.Type.Base == Types.NumberName && value.Raw is double number)
                {
                    if (spec.Min is double min && number < min)
                        problems.Add(new ParameterProblem(ParameterProblemCode.Min, spec.Key,
                            $"The parameter \"{spec.Name}\" must be at least {StringConverter.NumberToString(min)}."));

                    if (spec.Max is double max && number > max)
                        problems.Add(new ParameterProblem(ParameterProblemCode.Max, spec.Key,
                            $"The parameter \"{spec.Name}\" must be at most {StringConverter.NumberToString(max)}."));
                }

                if (spec.Options != null && !spec.Options.Any(option => CoreFunctions.ValuesEqual(option, value)))
                {
                    string allowed = string.Join(", ", spec.Options.Select(StringConverter.ValueToString));
                    problems.Add(new ParameterProblem(ParameterProblemCode.Option, spec.Key, $"The parameter \"{spec.Name}\" must be one of: {allowed}."));
                }

                normalized.InitVariable(spec.Key, value);
            }

            return problems.Count > 0
                ? new ParameterNormalization(null, problems)
                : new ParameterNormalization(normalized, []);
        }

        /// <inheritdoc cref="Normalize(IReadOnlyList{ParameterSpec}, IEnumerable{KeyValuePair{string, IValue}}?, IEnvironment?)"/>
        public static ParameterNormalization Normalize(IReadOnlyList<ParameterSpec> specs, IObject? given, IEnvironment? environment = null)
            => Normalize(specs, given == null ? null : Entries(given), environment);

        /// <summary>
        /// The parameter key of a normalized Object (docs/implementation/formula_models.md, "Parameter key"): its
        /// serialization with the initial values of the serialization variables, e.g. <c>{ period: 14 }</c>.
        /// </summary>
        public static string Key(IObject parameters) => StringConverter.ValueToString(Value.Object(parameters, ""));

        internal static IEnumerable<KeyValuePair<string, IValue>> Entries(IObject obj)
            => obj.Keys.Select(key => new KeyValuePair<string, IValue>(key, obj.GetProperty(key).GetValue())).ToArray();
    }
}
