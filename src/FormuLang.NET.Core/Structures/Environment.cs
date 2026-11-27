using FormuLang.NET.Abstractions.Functions;
using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Utils;

namespace FormuLang.NET.Core.Structures
{
    /// <summary>
    /// Global layer between FormuLang and the host application (docs/core/environment.md).
    /// The environment is never changed by a formula: every execution works on a copy of its variables in a <see cref="Context"/>.
    /// </summary>
    public class Environment : Primitives.Object, IEnvironment
    {
        /// <summary>
        /// Identifier of the implemented Core standard version.
        /// </summary>
        public const string CoreStandard = FormuLang.NET.Core.Standards.CoreStandard.Identifier;

        /// <summary>
        /// Name of the environment constant with the current time: Unix time in milliseconds, UTC.
        /// </summary>
        public const string NowConstant = "NOW";

        /// <summary>
        /// Clock of the <c>NOW</c> constant. Every pre-execution reads it once (docs/semantics/pre-execution.md), so
        /// <c>NOW</c> does not change during an execution. <c>null</c>: <c>NOW</c> is the value of the environment
        /// constant, set by the host application with <see cref="SetNow"/>.
        /// </summary>
        public TimeProvider? TimeProvider { get; set; } = TimeProvider.System;

        private readonly List<string> _standards = [];
        private readonly List<string> _extendedTypes = [];
        private readonly AbstractTree<string> _typeTree = new(Types.ObjectName, Types.ObjectName);
        private readonly Dictionary<string, IFunction> _functions = new(StringComparer.Ordinal);

        public IEnumerable<string> Standards => _standards;

        public IEnumerable<string> ExtendedTypes => _extendedTypes;

        /// <summary>
        /// Inheritance tree of extended types. The root is <c>Object</c>; nodes are keyed by the extended type name.
        /// </summary>
        public IAbstractTree<string> ExtendedTypeTree => _typeTree;

        public IEnumerable<IFunction> Functions => _functions.Values;

        public bool Supports(string standard) => _standards.Contains(standard);

        public IFunction? GetFunction(string name) => _functions.GetValueOrDefault(name);

        protected Environment() { }

        /// <summary>
        /// Creates an environment that supports the Core standard: its functions, procedures, constants and variables
        /// (docs/standards/core/README.md, "Environment structure").
        /// </summary>
        public static Environment Create()
        {
            var env = new Environment();

            // Constants
            env.InitConstant("STANDARDS", Value.Array([], "STANDARDS"));
            env.InitConstant("E", Value.Number(Math.E, "E"));
            env.InitConstant("PI", Value.Number(Math.PI, "PI"));
            env.InitConstant("PHI", Value.Number((1 + Math.Sqrt(5)) / 2, "PHI"));
            env.InitConstant(NowConstant, NowValue(TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds()));

            // Keyword constants
            env.InitConstant("TRUE", Value.Boolean(true, "TRUE"));
            env.InitConstant("FALSE", Value.Boolean(false, "FALSE"));
            env.InitConstant("NULL", Value.Null("NULL"));

            // Variables
            env.InitVariable("array_indexing", Value.Number(0, "0"));
            env.InitVariable("suppress_errors", Value.Boolean(false, "false"));
            env.InitVariable(FormuLang.NET.Core.Standards.CoreFunctions.TimeOffsetName, Value.Number(0, "0"));

            // Serialization variables
            env.InitVariable(SerializationOptions.DecimalPlaceName, Value.Number(-1, "-1"));
            env.InitVariable(SerializationOptions.SplitArrayName, Value.Boolean(false, "false"));
            env.InitVariable(SerializationOptions.SplitObjectName, Value.Boolean(false, "false"));
            env.InitVariable(SerializationOptions.IncludeObjectClassName, Value.Boolean(false, "false"));
            env.InitVariable(SerializationOptions.MaxArrayPreviewName, Value.Number(-1, "-1"));

            // core-1.2 is backward compatible: STANDARDS lists core-1.0 and core-1.1 too, so REQUIRE "core-1.0" keeps working
            foreach (string compatible in FormuLang.NET.Core.Standards.CoreStandard.CompatibleIdentifiers)
                env.RegisterStandard(compatible);
            env.RegisterStandard(FormuLang.NET.Core.Standards.CoreStandard.Instance);

            return env;
        }

        /// <summary>
        /// Pins the <c>NOW</c> constant to a point in time (Unix time in milliseconds, UTC) and stops reading the clock
        /// (<see cref="TimeProvider"/> becomes <c>null</c>), e.g. for deterministic tests.
        /// </summary>
        public void SetNow(long unixTimeMilliseconds)
        {
            TimeProvider = null;
            SetConstant(NowConstant, NowValue(unixTimeMilliseconds));
        }

        /// <summary>
        /// The value of <c>NOW</c> for a new execution: the current time of <see cref="TimeProvider"/>, or the value of
        /// the <c>NOW</c> constant of the environment. <c>null</c> if the environment has neither.
        /// </summary>
        public static IValue? ReadNow(IEnvironment environment)
        {
            if (environment is Environment { TimeProvider: TimeProvider clock })
                return NowValue(clock.GetUtcNow().ToUnixTimeMilliseconds());

            if (environment.HasKey(NowConstant))
            {
                var container = environment.GetProperty(NowConstant);
                if (container.ContainerType == ContainerType.Constant && container.CanAccessValue)
                    return container.GetValue();
            }

            return null;
        }

        internal static IValue NowValue(double unixTimeMilliseconds)
            => Value.Number(unixTimeMilliseconds, StringConverter.NumberToString(unixTimeMilliseconds));

        /// <summary>
        /// Adds a standard: its identifier, extended types and named functions.
        /// </summary>
        /// <exception cref="InvalidOperationException">A function of the standard is already defined.</exception>
        public void RegisterStandard(IStandard standard)
        {
            foreach (string type in standard.ExtendedTypes)
            {
                var node = standard.ExtendedTypeTree.GetNode(type);
                string? parent = node?.Parent?.Key;
                RegisterExtendedType(type, parent == null || parent == Types.ObjectName ? null : parent);
            }

            foreach (IFunction function in standard.NamedFunctions)
                RegisterFunction(function);

            RegisterStandard($"{standard.ID}-{standard.Version.Version}");
        }

        /// <summary>
        /// Adds a standard identifier, e.g. <c>stock-0.1</c>, to <c>STANDARDS</c>.
        /// </summary>
        public void RegisterStandard(string standard)
        {
            if (_standards.Contains(standard))
                return;

            _standards.Add(standard);

            SetConstant("STANDARDS", Value.Array(_standards.Select(s => Value.Text(s, $"\"{s}\"")), "STANDARDS"));
        }

        /// <summary>
        /// Adds a named function or procedure.
        /// </summary>
        /// <exception cref="InvalidOperationException">A function with the same name is already defined.</exception>
        public void RegisterFunction(IFunction function)
        {
            if (!_functions.TryAdd(function.Name, function))
                throw new InvalidOperationException($"Function \"{function.Name}\" is already defined.");
        }

        /// <summary>
        /// Number of the named functions and procedures; a prepared formula is prepared again when it changes.
        /// </summary>
        public int FunctionCount => _functions.Count;

        private readonly Dictionary<string, Processing.MetadataChecker> _metadataCheckers = new(StringComparer.Ordinal);

        /// <summary>
        /// Adds the check of a top-level key of the metadata (docs/core/metadata.md, "Metadata keys"): the key of an
        /// extension standard (its name in lowercase, e.g. <c>stock</c>) or <c>x</c> of the host application. A value of
        /// a wrong type is a <c>METADATA_TYPE</c> warning.
        /// </summary>
        /// <exception cref="InvalidOperationException">The key already has a check.</exception>
        public void RegisterMetadataChecker(string key, Processing.MetadataChecker checker)
        {
            if (!_metadataCheckers.TryAdd(key, checker))
                throw new InvalidOperationException($"Metadata key \"{key}\" already has a check.");
        }

        /// <summary>
        /// The check of a top-level metadata key, or <c>null</c>.
        /// </summary>
        public Processing.MetadataChecker? GetMetadataChecker(string key) => _metadataCheckers.GetValueOrDefault(key);

        /// <summary>
        /// Registers an extended type, e.g. <c>RegisterExtendedType("Stock.Candle")</c>.
        /// </summary>
        /// <param name="parentType">Extended type it derives from; <c>null</c> to derive directly from Object.</param>
        public void RegisterExtendedType(string extendedType, string? parentType = null)
        {
            _typeTree.AddNode(extendedType, extendedType, parentType ?? Types.ObjectName);
            _extendedTypes.Add(extendedType);
        }
    }
}
