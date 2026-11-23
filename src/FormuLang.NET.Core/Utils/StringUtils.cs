using System.Text;

namespace FormuLang.NET.Core.Utils
{
    /// <summary>
    /// Character classification of the FormuLang standard (docs/syntax/symbols.md).
    /// The code of a formula allows only ASCII characters, so these checks are ASCII-only on purpose; text literals
    /// allow every Unicode text character (<see cref="IsTextCharacter"/>).
    /// </summary>
    internal static class StringUtils
    {
        /// <summary>
        /// Text character (docs/syntax/symbols.md, "Text characters"): every Unicode scalar value except the control
        /// characters U+0000 - U+001F and U+007F - U+009F. [Tab] is allowed.
        /// </summary>
        public static bool IsTextCharacter(int codePoint)
            => codePoint == '\t' || (codePoint >= 0x20 && codePoint < 0x7F) || (codePoint > 0x9F && Rune.IsValid(codePoint));

        /// <summary>
        /// Splits a text into its characters: Unicode code points (docs/standards/core/types/Text.md, "Characters").
        /// </summary>
        public static string[] CodePoints(string text)
        {
            var result = new List<string>(text.Length);
            foreach (Rune rune in text.EnumerateRunes())
                result.Add(rune.ToString());
            return [.. result];
        }

        /// <summary>
        /// Number of characters (Unicode code points) of a text.
        /// </summary>
        public static int CodePointLength(string text)
        {
            int count = 0;
            foreach (Rune _ in text.EnumerateRunes())
                count++;
            return count;
        }

        /// <summary>
        /// Compares texts by their code points (ordinal UTF-16 comparison orders surrogate pairs differently).
        /// </summary>
        public static int CompareCodePoints(string a, string b)
        {
            StringRuneEnumerator x = a.EnumerateRunes(), y = b.EnumerateRunes();

            while (true)
            {
                bool hasX = x.MoveNext(), hasY = y.MoveNext();
                if (!hasX || !hasY)
                    return hasX.CompareTo(hasY);

                int compare = x.Current.Value.CompareTo(y.Current.Value);
                if (compare != 0)
                    return compare;
            }
        }
        /// <summary>
        /// [Tab], [New line], [Space] and the printable ASCII characters 33-126.
        /// </summary>
        public static bool IsValidSymbol(char c)
        {
            return c == '\t' || c == '\n' || (c >= ' ' && c <= '~');
        }

        public static bool IsCharUppercaseLetter(char c) => c >= 'A' && c <= 'Z';

        public static bool IsCharLowercaseLetter(char c) => c >= 'a' && c <= 'z';

        public static bool IsCharLetter(char c) => IsCharUppercaseLetter(c) || IsCharLowercaseLetter(c);

        public static bool IsCharDigit(char c) => c >= '0' && c <= '9';

        public static bool IsCharHexDigit(char c)
            => IsCharDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        /// <summary>
        /// Word characters: letters, digits and underline.
        /// </summary>
        public static bool IsCharWordCharacter(char c) => IsCharLetter(c) || IsCharDigit(c) || c == '_';

        /// <summary>
        /// An identifier starts with a letter or an underline.
        /// </summary>
        public static bool IsCharIdentifierStart(char c) => IsCharLetter(c) || c == '_';

        /// <summary>
        /// White spaces inside a line: [Space] and [Tab].
        /// </summary>
        public static bool IsCharWhitespace(char c) => c == ' ' || c == '\t';

        /// <summary>
        /// Printable ASCII characters that are neither word characters nor white spaces.
        /// </summary>
        public static bool IsCharOperator(char c)
        {
            return c != '_' && (
                   (c >= '!' && c <= '/')   // !"#$%&'()*+,-./
                || (c >= ':' && c <= '@')   // :;<=>?@
                || (c >= '[' && c <= '`')   // [\]^`
                || (c >= '{' && c <= '~')); // {|}~
        }

        public static bool IsStringWhitespace(string s) => string.IsNullOrWhiteSpace(s);
    }
}
