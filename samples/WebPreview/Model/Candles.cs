namespace WebPreview.Model
{
    /// <summary>Mock candles: a seeded random walk with trend regimes.</summary>
    public static class Candles
    {
        /// <summary>Deterministic pseudo-random numbers (mulberry32), so a seed always gives the same chart.</summary>
        /// <remarks>The same generator as the TypeScript preview: a seed gives the same candles in both previews.</remarks>
        private static Func<double> Random(int seed)
        {
            uint state = (uint)seed;
            return () =>
            {
                state += 0x6D2B79F5;
                uint t = state;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return (t ^ (t >> 14)) / 4294967296.0;
            };
        }

        private const double Day = 24 * 60 * 60 * 1000;
        private static readonly double Start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        /// <summary><c>Math.round</c> of JavaScript: halves are rounded up.</summary>
        private static double RoundHalfUp(double value) => Math.Floor(value + 0.5);

        private static double Round(double value) => RoundHalfUp(value * 100) / 100;

        /// <summary>
        /// Mock daily candles: a random walk with trend regimes (up, down, sideways) of random length and volatility,
        /// so the chart has crossovers, breakouts and candle patterns.
        /// </summary>
        public static IReadOnlyList<ChartCandle> Generate(int count, int seed)
        {
            Func<double> next = Random(seed);
            double Gaussian()
            {
                double u = Math.Max(next(), 1e-9);
                return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * next());
            }

            var candles = new List<ChartCandle>(count);
            double close = 80 + next() * 60;
            double drift = 0;
            double volatility = 0.015;
            int regime = 0;

            for (int i = 0; i < count; i++)
            {
                if (regime-- <= 0)
                {
                    regime = 12 + (int)Math.Floor(next() * 30);
                    drift = (next() - 0.5) * 0.012;
                    volatility = 0.008 + next() * 0.022;
                }

                double open = Round(close * (1 + Gaussian() * volatility * 0.25));
                close = Round(Math.Max(1, open * (1 + drift + Gaussian() * volatility)));
                double high = Round(Math.Max(open, close) * (1 + Math.Abs(Gaussian()) * volatility * 0.6));
                double low = Round(Math.Min(open, close) * (1 - Math.Abs(Gaussian()) * volatility * 0.6));
                double volume = RoundHalfUp(1000 + next() * 4000 + Math.Abs(close - open) / open * 200000);

                candles.Add(new ChartCandle(
                    Start + i * Day,
                    open,
                    Math.Max(high, Math.Max(open, close)),
                    Math.Min(low, Math.Min(open, close)),
                    close,
                    volume));
            }

            return candles;
        }

        public static int RandomSeed() => System.Random.Shared.Next(1_000_000);

        /// <summary>The date of a candle, <c>yyyy-MM-dd</c>.</summary>
        public static string DateOf(IReadOnlyList<ChartCandle> candles, int index)
            => DateTimeOffset.FromUnixTimeMilliseconds(index >= 0 && index < candles.Count ? (long)candles[index].Time : 0)
                .ToString("yyyy-MM-dd");
    }
}
