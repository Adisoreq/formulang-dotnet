using FormuLang.NET.Abstractions.Operators;

namespace FormuLang.NET.Abstractions.Processing
{
    public interface IToken
    {
        public TokenType Type { get; }
        public string Value { get; }

        /// <summary>
        /// Line of the first character of the token (from 1), or <c>null</c> if the tokenizer does not record positions.
        /// </summary>
        public int? Line => null;

        /// <summary>
        /// Column of the first character of the token (from 1), or <c>null</c> if the tokenizer does not record positions.
        /// </summary>
        public int? Column => null;

        public bool IsOperator() => Type == TokenType.Operator;
    }
}
