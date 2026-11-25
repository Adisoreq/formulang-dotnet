using FormuLang.NET.Abstractions.Primitives;

namespace FormuLang.NET.Core.Primitives
{
    public abstract class Primitive : IPrimitive
    {
        public PrimitiveType PrimitiveType { get; protected set; }
        public IType Type { get; protected set; }
        private string? _expression;

        /// <summary>
        /// Text of the expression of the primitive. A primitive created without one (<c>null</c>) gets it from
        /// <see cref="FormatExpression"/> when it is first read.
        /// </summary>
        public string Expression
        {
            get => _expression ??= FormatExpression();
            protected set => _expression = value;
        }

        protected Primitive(PrimitiveType primitiveType, IType type, string? expression)
        {
            this.PrimitiveType = primitiveType;
            this.Type = type;
            _expression = expression;
        }

        /// <summary>
        /// The expression of a primitive created without one.
        /// </summary>
        protected virtual string FormatExpression() => string.Empty;

        public abstract bool CanAccessValue { get; }
        public abstract IError CanUpdateValue { get; }

        public abstract IValue GetValue();
        public abstract IError SetValue(IValue value);
    }
}
