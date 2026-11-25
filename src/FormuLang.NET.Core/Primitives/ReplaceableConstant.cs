using FormuLang.NET.Abstractions.Primitives;

namespace FormuLang.NET.Core.Primitives
{
    /// <summary>
    /// Constant whose value the host side replaces between executions, e.g. <c>PREV</c> before every iteration. A formula
    /// still cannot change it; the container is reused, so a new iteration does not create a new property.
    /// </summary>
    internal sealed class ReplaceableConstant : Constant
    {
        public ReplaceableConstant(string name, IValue value) : base(name, value) { }

        public void Replace(IValue value) => Store(value);
    }
}
