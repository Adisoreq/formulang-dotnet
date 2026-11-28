using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;

namespace FormuLang.NET.Abstractions.Functions
{
    public interface IArgumentTemplate : INamedStructure
    {
        /// <summary>
        /// Type of the argument primitive
        /// </summary>
        public IType Type { get; }

        /// <summary>
        /// Base type of every element, if the type is Array (e.g. <c>Array&lt;Number&gt;</c>); null for no requirement
        /// </summary>
        public IType? ElementType { get; }

        /// <summary>
        /// Determines whether matching the type should be ignored
        /// </summary>
        public bool AnyType { get; }

        /// <summary>
        /// Determines whether the template is applied to multiple arguments
        /// </summary>
        public bool Multiple { get; }

        /// <summary>
        /// Determines whether the argument may be omitted
        /// </summary>
        public bool Optional { get; }

        /// <summary>
        /// Determines whether the argument, or with an <see cref="ElementType"/> every element, may also be <c>null</c>:
        /// the base types <c>Number, Null</c> of a collection template (since <c>core-1.3</c>,
        /// docs/standards/core/README.md, "Missing values"). <c>false</c> by default.
        /// </summary>
        public bool Nullable => false;
    }
}
