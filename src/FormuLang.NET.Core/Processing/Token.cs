using FormuLang.NET.Abstractions.Processing;
using FormuLang.NET.Abstractions.Operators;

namespace FormuLang.NET.Core.Processing
{
    internal class Token : IToken
    {
        public TokenType Type { get; private set; }
        public string Value { get; private set; }

        /// <summary>
        /// Line of the first character of the token, from 1 (the <see cref="Tokenizer"/> gives the line of the text it
        /// tokenizes; the executor maps it to the original formula).
        /// </summary>
        public int? Line { get; }

        /// <summary>
        /// Column of the first character of the token, from 1.
        /// </summary>
        public int? Column { get; }

        internal Token(TokenType type, string value, int? line = null, int? column = null)
        {
            Type = type;
            Value = value;
            Line = line;
            Column = column;
        }

        private string DisplayValue => 
            Value.Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");

        public override string ToString()
        {
            return $"({Type}: '{DisplayValue}')";
        }
    }
}
