using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Functions;
using FormuLang.NET.Core.Primitives;
using System.Text.RegularExpressions;
using static FormuLang.NET.Core.Standards.FunctionHelpers;

namespace FormuLang.NET.Core.Standards
{
    /// <summary>
    /// Built-in procedures of the Core standard (docs/standards/core/procedures): <c>SET</c>, <c>INIT</c>,
    /// <c>COND_SET</c>, <c>REQUIRE</c>, and since <c>core-1.2</c> <c>STORE</c> and <c>CLEAR</c>.
    /// Every procedure returns the <c>SUCCESS</c> Error when it finishes correctly.
    /// </summary>
    public static partial class CoreProcedures
    {
        public static IReadOnlyList<Procedure> All { get; } =
        [
            VariableProcedure("SET", Set),
            VariableProcedure("INIT", Init),
            VariableProcedure("COND_SET", ConditionalSet),
            new Procedure("REQUIRE", parameterCount: 1, takesVariable: false,
            [
                Overload((a, c) => Require([Txt(a[0])], c), TextArg("standard")),
                Overload((a, c) => Require(Arr(a[0]).Select(Txt), c), ArgumentTemplate.ArrayOf("standards", Types.Text)),
            ]),
            new Procedure("STORE", parameterCount: 2, takesVariable: false,
            [
                Overload((a, c) => Store(Txt(a[0]), a[1], c), TextArg("key"), AnyArg("value")),
            ]) { WritesMemoryOnly = true },
            new Procedure("CLEAR", parameterCount: 1, takesVariable: false,
            [
                Overload((a, c) => Clear(Txt(a[0]), c), TextArg("key")),
                Overload((_, c) => Clear(null, c)),
            ]) { WritesMemoryOnly = true },
        ];

        // Memory (core-1.2): docs/core/context.md, "Memory"

        /// <summary>
        /// The memory of the context, or the <c>OPERATION_ERROR</c> of a context without one (the Default Context).
        /// </summary>
        internal static IMemory? MemoryOf(IContext context, string name, out IValue? error)
        {
            IMemory? memory = context.Memory;
            error = memory == null
                ? OperationError($"\"{name}\" can be used only in a context with a memory (an Iteration or Time Context).")
                : null;
            return memory;
        }

        // STORE(key, value): docs/standards/core/procedures/STORE.md
        private static IValue Store(string key, IValue value, IContext context)
        {
            if (MemoryOf(context, "STORE", out IValue? error) is not IMemory memory)
                return error!;

            if (value.Type.Base == Types.DelegateName)
                return ArgumentError("A Delegate cannot be stored in the memory.");

            memory.Store(key, value);
            return Success();
        }

        // CLEAR(key) and CLEAR(): docs/standards/core/procedures/CLEAR.md
        private static IValue Clear(string? key, IContext context)
        {
            if (MemoryOf(context, "CLEAR", out IValue? error) is not IMemory memory)
                return error!;

            memory.Clear(key);
            return Success();
        }

        private delegate IError VariableOperation(IContext context, VariableReference variable, IValue value);

        private static Procedure VariableProcedure(string name, VariableOperation operation)
            => new(name, parameterCount: 2, takesVariable: true,
            [
                Overload((a, c) => ErrorValue(operation(c, (VariableReference)a[0].Raw!, a[1])),
                    ArgumentTemplate.Of("variable", Types.Variable), AnyArg("value")),
            ]);

        // SET

        private static IError Set(IContext context, VariableReference variable, IValue value)
        {
            if (variable.Scope == VariableScope.Environment)
                return SetEnvironmentVariable(context, variable.Name, value);

            if (context.HasKey(variable.Name))
                return context.SetProperty(variable.Name, value);

            if (context.Environment.HasKey(variable.Name) || context.EnvironmentVariables.HasKey(variable.Name))
                return Errors.InvalidIdentifierError($"\"{variable.Name}\" symbol is already defined by environment.");

            context.InitVariable(variable.Name, value);
            return Errors.Success();
        }

        private static IError SetEnvironmentVariable(IContext context, string name, IValue value)
        {
            if (!context.EnvironmentVariables.HasKey(name))
                return Errors.EnvironmentError($"Environment variable \"{name}\" is not defined.");

            return context.EnvironmentVariables.SetProperty(name, value);
        }

        // INIT

        private static IError Init(IContext context, VariableReference variable, IValue value)
        {
            if (variable.Scope == VariableScope.Environment)
            {
                return context.EnvironmentVariables.HasKey(variable.Name)
                    ? Errors.Success()
                    : Errors.EnvironmentError($"Environment variable \"{variable.Name}\" is not defined.");
            }

            if (!context.HasKey(variable.Name))
                return Set(context, variable, value);

            if (!context.GetProperty(variable.Name).CanAccessValue)
                return context.SetProperty(variable.Name, value);

            return Errors.Success();
        }

        // COND_SET

        private static IError ConditionalSet(IContext context, VariableReference variable, IValue value)
        {
            IObject scope = variable.Scope == VariableScope.Environment ? context.EnvironmentVariables : context;

            return scope.HasKey(variable.Name)
                ? scope.SetProperty(variable.Name, value)
                : Errors.Success();
        }

        // REQUIRE

        [GeneratedRegex(@"^([a-zA-Z_]\w*)-((?:\d+\+?|x)(?:\.(?:\d+\+?|x))*)$", RegexOptions.CultureInvariant)]
        private static partial Regex RequirementPattern();

        [GeneratedRegex(@"^([a-zA-Z_]\w*)-(\d+(?:\.\d+)*)$", RegexOptions.CultureInvariant)]
        private static partial Regex StandardPattern();

        private static IValue Require(IEnumerable<string> requirements, IContext context)
        {
            string[] supported = context.Environment.HasKey("STANDARDS") && context.Environment.GetProperty("STANDARDS").GetValue().Raw is IValue[] standards
                ? standards.Select(s => s.Raw as string ?? "").ToArray()
                : [];

            foreach (string requirement in requirements)
            {
                Match match = RequirementPattern().Match(requirement);

                if (!match.Success)
                    return OperationError($"\"{requirement}\" is not a valid standard requirement.");

                string name = match.Groups[1].Value;
                string[] pattern = match.Groups[2].Value.Split('.');

                if (!supported.Any(standard => Satisfies(standard, name, pattern)))
                    return ErrorValue(Errors.NotSupportedError($"Standard \"{requirement}\" is not supported."));
            }

            return Success();
        }

        /// <summary>
        /// Segments: <c>n</c> exactly n, <c>n+</c> n or higher, <c>x</c> any. Missing segments are <c>x</c>.
        /// </summary>
        public static bool Satisfies(string standard, string name, string[] pattern)
        {
            Match match = StandardPattern().Match(standard);

            if (!match.Success || match.Groups[1].Value != name)
                return false;

            long[] version = match.Groups[2].Value.Split('.').Select(long.Parse).ToArray();

            for (int i = 0; i < pattern.Length; i++)
            {
                string segment = pattern[i];
                long actual = i < version.Length ? version[i] : 0;

                if (segment == "x")
                    continue;

                bool orHigher = segment.EndsWith('+');
                long expected = long.Parse(orHigher ? segment[..^1] : segment);

                if (orHigher ? actual < expected : actual != expected)
                    return false;
            }

            return true;
        }
    }
}
