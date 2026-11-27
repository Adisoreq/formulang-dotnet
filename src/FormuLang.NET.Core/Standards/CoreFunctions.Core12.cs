using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Functions;
using FormuLang.NET.Core.Primitives;
using static FormuLang.NET.Core.Standards.FunctionHelpers;

namespace FormuLang.NET.Core.Standards
{
    public static partial class CoreFunctions
    {
        /// <summary>
        /// Functions added in <c>core-1.2</c>: <c>RECALL</c> (memory of the context). <c>AND</c> and <c>OR</c> with many
        /// arguments and a short circuit are <see cref="ShortCircuitFunction"/>s in the logic functions.
        /// </summary>
        private static IEnumerable<Function> Core12Functions() =>
        [
            // RECALL reads the memory as it was at the start of the iteration, so it is not a side effect
            Fn("RECALL", Overload((a, c) => Recall(Txt(a[0]), a.Count > 1 ? a[1] : Value.Null("null"), c),
                TextArg("key"), ArgumentTemplate.Any("fallback", optional: true))),
        ];

        // RECALL(key, fallback): docs/standards/core/functions/RECALL.md
        private static IValue Recall(string key, IValue fallback, IContext context)
        {
            if (CoreProcedures.MemoryOf(context, "RECALL", out IValue? error) is not IMemory memory)
                return error!;

            return memory.Recall(key) ?? fallback;
        }
    }

    /// <summary>
    /// <c>AND</c> and <c>OR</c> (docs/standards/core/functions/AND.md, "Short circuit", since <c>core-1.2</c>): the
    /// arguments are checked from the left, and the first <c>false</c> (<c>AND</c>) or <c>true</c> (<c>OR</c>) decides the
    /// result. An Error before the deciding argument is returned; the arguments after it are not used, so an Error or a
    /// value that is not a Boolean there is discarded. Without a deciding argument the overloads apply as for every
    /// function.
    /// </summary>
    public sealed class ShortCircuitFunction : Function
    {
        private readonly bool _deciding;

        /// <param name="deciding">The value that decides the result: <c>false</c> for <c>AND</c>, <c>true</c> for <c>OR</c>.</param>
        private ShortCircuitFunction(string name, bool deciding, Func<IEnumerable<bool>, bool> operation)
            : base(name,
            [
                Overload((a, _) => B(operation([Bool(a[0]), Bool(a[1])])), BooleanArg("a"), BooleanArg("b")),
                // At least 2 arguments; with exactly 2 the first overload matches
                Overload((a, _) => B(operation(a.Select(Bool))), BooleanArg("a"),
                    ArgumentTemplate.Of("values", Types.Boolean, multiple: true)),
            ])
        {
            _deciding = deciding;
            SideEffectFree = true;
        }

        public static ShortCircuitFunction And { get; } = new("AND", false, values => values.All(v => v));

        public static ShortCircuitFunction Or { get; } = new("OR", true, values => values.Any(v => v));

        public override IValue Invoke(IEnumerable<IValue> parameters, IContext context)
        {
            IValue[] args = parameters as IValue[] ?? parameters.ToArray();

            // With one argument no overload matches and nothing is decided
            if (args.Length >= 2)
            {
                foreach (IValue argument in args)
                {
                    string type = argument.Type.Base;

                    if (type == Types.ErrorName)
                        return argument;

                    if (type == Types.BooleanName && argument.Raw is bool value && value == _deciding)
                        return B(_deciding);
                }
            }

            return base.Invoke(args, context);
        }
    }
}
