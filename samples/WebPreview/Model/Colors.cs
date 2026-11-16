using System.Globalization;
using System.Text.RegularExpressions;

namespace WebPreview.Model
{
    /// <summary>A CSS color split into a <c>#rrggbb</c> color and an opacity, for the color inputs of the preview.</summary>
    public readonly record struct ColorParts(string Hex, double Alpha);

    public static partial class Colors
    {
        private static string Hex2(double value) => ((int)Math.Floor(value + 0.5)).ToString("x2");

        private static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

        [GeneratedRegex("^#([0-9a-f]{6})$")]
        private static partial Regex Long();

        [GeneratedRegex("^#([0-9a-f])([0-9a-f])([0-9a-f])$")]
        private static partial Regex Short();

        [GeneratedRegex(@"^rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+)\s*)?\)$")]
        private static partial Regex Rgb();

        /// <summary>Parses <c>#rrggbb</c>, <c>#rgb</c>, <c>rgb(...)</c>, <c>rgba(...)</c> and <c>transparent</c>.</summary>
        public static ColorParts Parse(string color)
        {
            string value = color.Trim().ToLowerInvariant();
            if (value == "transparent")
                return new("#000000", 0);

            Match match = Long().Match(value);
            if (match.Success)
                return new($"#{match.Groups[1].Value}", 1);

            match = Short().Match(value);
            if (match.Success)
            {
                string r = match.Groups[1].Value, g = match.Groups[2].Value, b = match.Groups[3].Value;
                return new($"#{r}{r}{g}{g}{b}{b}", 1);
            }

            match = Rgb().Match(value);
            if (match.Success)
            {
                return new(
                    $"#{Hex2(Number(match.Groups[1].Value))}{Hex2(Number(match.Groups[2].Value))}{Hex2(Number(match.Groups[3].Value))}",
                    match.Groups[4].Success ? Number(match.Groups[4].Value) : 1);
            }

            return new("#000000", 1);
        }

        /// <summary><c>#rrggbb</c> for an opaque color, <c>rgba(...)</c> otherwise.</summary>
        public static string Format(string hex, double alpha)
        {
            if (alpha >= 1)
                return hex;

            int r = int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber);
            int g = int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber);
            int b = int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber);
            return $"rgba({r}, {g}, {b}, {Names.Number(Math.Floor(alpha * 100 + 0.5) / 100)})";
        }
    }
}
