using FormuLang.NET.Abstractions.Functions;
using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Core.Primitives;

namespace FormuLang.NET.Core.Functions
{
    /// <summary>
    /// One parameter position of a function overload (docs/implementation/argument_template.md).
    /// </summary>
    public readonly record struct ArgumentTemplate : IArgumentTemplate
    {
        public required string Name { get; init; }

        public IType Type { get; init; }
        public IType? ElementType { get; init; }
        public bool AnyType { get; init; }
        public bool Multiple { get; init; }
        public bool Optional { get; init; }

        /// <summary>
        /// The argument (with an <see cref="ElementType"/>: every element) may also be <c>null</c>, e.g.
        /// <c>Number, Null</c> (since <c>core-1.3</c>, "Missing values").
        /// </summary>
        public bool Nullable { get; init; }

        public ArgumentTemplate()
        {
            Type = Types.Undefined;
        }

        public static ArgumentTemplate Of(string name, IType type, bool optional = false, bool multiple = false, bool nullable = false)
            => new() { Name = name, Type = type, Optional = optional, Multiple = multiple, Nullable = nullable };

        /// <summary>
        /// Array whose every element has the given base type, e.g. <c>numbers &lt;Array&lt;Number&gt;&gt;</c>; with
        /// <paramref name="nullable"/> the elements may also be <c>null</c> (<c>Array&lt;Number | Null&gt;</c>).
        /// </summary>
        public static ArgumentTemplate ArrayOf(string name, IType elementType, bool optional = false, bool nullable = false)
            => new() { Name = name, Type = Types.Array, ElementType = elementType, Optional = optional, Nullable = nullable };

        public static ArgumentTemplate Any(string name, bool optional = false, bool multiple = false)
            => new() { Name = name, AnyType = true, Optional = optional, Multiple = multiple };

        public override string ToString()
        {
            string orNull = Nullable ? $" | {Types.NullName}" : "";
            string typeStr = AnyType ? "Any"
                : ElementType != null ? $"Array<{ElementType.Base}{orNull}>"
                : (Type.HasExtension() ? Type.Extended! : Type.Base) + orNull;

            string mul = Multiple ? "..." : "";
            string opt = Optional ? "?" : "";

            return $"{mul}{Name}{opt} <{typeStr}>";
        }
    }
}
