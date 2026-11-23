namespace WebPreview.Tests
{
    /// <summary>Mock candles: deterministic and well-formed.</summary>
    public class CandlesTests
    {
        [Fact]
        public void SameSeedGivesSameCandles()
            => Assert.Equal(Candles.Generate(50, 42), Candles.Generate(50, 42));

        [Fact]
        public void DifferentSeedsGiveDifferentCandles()
            => Assert.NotEqual(Candles.Generate(50, 1), Candles.Generate(50, 2));

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(160)]
        public void GeneratesRequestedCount(int count)
            => Assert.Equal(count, Candles.Generate(count, 7).Count);

        [Fact]
        public void CandlesAreConsistent()
        {
            var candles = Candles.Generate(500, 20251);
            Assert.All(candles, c =>
            {
                Assert.True(c.High >= Math.Max(c.Open, c.Close));
                Assert.True(c.Low <= Math.Min(c.Open, c.Close));
                Assert.True(c.Volume > 0);
            });
        }

        [Fact]
        public void CandlesAreOneDayApartFrom2025()
        {
            var candles = Candles.Generate(3, 5);
            Assert.Equal("2025-01-01", Candles.DateOf(candles, 0));
            Assert.Equal("2025-01-03", Candles.DateOf(candles, 2));
            Assert.Equal("1970-01-01", Candles.DateOf(candles, 99));
        }
    }

    public class ColorsTests
    {
        [Theory]
        [InlineData("#AABBCC", "#aabbcc", 1)]
        [InlineData("#abc", "#aabbcc", 1)]
        [InlineData("rgb(255, 0, 16)", "#ff0010", 1)]
        [InlineData("rgba(61, 220, 151, 0.14)", "#3ddc97", 0.14)]
        [InlineData("transparent", "#000000", 0)]
        [InlineData("not a color", "#000000", 1)]
        public void ParsesCssColors(string css, string hex, double alpha)
            => Assert.Equal(new ColorParts(hex, alpha), Colors.Parse(css));

        [Fact]
        public void FormatsOpaqueColorAsHex()
            => Assert.Equal("#123456", Colors.Format("#123456", 1));

        [Fact]
        public void FormatsTranslucentColorAsRgba()
            => Assert.Equal("rgba(61, 220, 151, 0.14)", Colors.Format("#3ddc97", 0.14));

        [Theory]
        [InlineData("rgba(10, 20, 30, 0.5)")]
        [InlineData("#e9c46a")]
        public void ParseAndFormatRoundTrip(string css)
        {
            ColorParts parts = Colors.Parse(css);
            Assert.Equal(css, Colors.Format(parts.Hex, parts.Alpha));
        }
    }

    public class NamesTests
    {
        [Theory]
        [InlineData("12.5", 12.5)]
        [InlineData("  3 ", 3)]
        [InlineData("", 0)]
        [InlineData(null, 0)]
        [InlineData("1e3", 1000)]
        public void ParsesNumbers(string? text, double expected)
            => Assert.Equal(expected, Names.Parse(text));

        [Fact]
        public void ParseOfTextIsNaN()
            => Assert.True(double.IsNaN(Names.Parse("abc")));

        [Fact]
        public void ClassesSkipsEmptyOnes()
        {
            Assert.Equal("a c", Names.Classes("a", null, "", "c"));
            Assert.Null(Names.Classes(null, ""));
        }

        [Fact]
        public void CssIsLowerCaseEnumName()
            => Assert.Equal("histogram", Names.Css(FillMode.Histogram));
    }
}
