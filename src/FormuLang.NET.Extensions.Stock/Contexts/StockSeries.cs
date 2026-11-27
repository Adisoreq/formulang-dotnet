using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Core.Utils;
using System.Runtime.CompilerServices;

namespace FormuLang.NET.Extensions.Stock.Contexts
{
    /// <summary>
    /// Candles of another timeframe, provided by the host application (docs/standards/stock/README.md, "Other
    /// timeframes", since <c>stock-1.2</c>).
    /// </summary>
    /// <param name="Length">Length of a candle of the timeframe, in milliseconds.</param>
    /// <param name="Candles">
    /// The candles, ordered from the oldest, closed at the end of the last candle of the main chart. Every candle needs
    /// its <c>time</c> (its opening time).
    /// </param>
    public sealed record TimeframeCandles(double Length, IEnumerable<Candle> Candles);

    /// <summary>
    /// Options of a Stock Context (since <c>stock-1.2</c>).
    /// </summary>
    public sealed class StockContextOptions
    {
        /// <summary>
        /// Executor of the formulas loaded with <c>LOAD_INDICATOR</c> and <c>LOAD_PATTERN</c>; the Core executor by default.
        /// </summary>
        public Executor? Executor { get; init; }

        /// <summary>
        /// The <c>TIMEFRAME</c> constant: the timeframe of the candles (e.g. <c>"15m"</c>), or <c>null</c> (default).
        /// </summary>
        public string? Timeframe { get; init; }

        /// <summary>
        /// The <c>INSTRUMENT</c> constant: the instrument of the candles (e.g. <c>"EURUSD"</c>), or <c>null</c> (default).
        /// </summary>
        public string? Instrument { get; init; }

        /// <summary>
        /// Length of a candle of the main chart, in milliseconds; needed with <see cref="Timeframes"/>.
        /// </summary>
        public double? TimeframeLength { get; init; }

        /// <summary>
        /// Candles of other timeframes by name, for <c>LOAD_CANDLES</c> and the <c>timeframe</c> option of
        /// <c>LOAD_INDICATOR</c> and <c>LOAD_PATTERN</c>.
        /// </summary>
        public IReadOnlyDictionary<string, TimeframeCandles>? Timeframes { get; init; }

        /// <summary>
        /// Candles of other timeframes, asked the first time a formula uses a timeframe (<c>null</c>: the timeframe is not
        /// provided, a <c>NOT_SUPPORTED_ERROR</c>). Used when <see cref="Timeframes"/> is <c>null</c>.
        /// </summary>
        public Func<string, TimeframeCandles?>? TimeframeProvider { get; init; }

        internal bool HasTimeframes => Timeframes != null || TimeframeProvider != null;
    }

    /// <summary>
    /// The candles of the timeframes that the host application provides, shared by every series of one Stock Context.
    /// </summary>
    internal sealed class Timeframes(StockContextOptions options)
    {
        private readonly Dictionary<string, StockSeries?> _loaded = new(StringComparer.Ordinal);

        public StockSeries? Get(string name, StockSeries parent)
        {
            if (_loaded.TryGetValue(name, out StockSeries? known))
                return known;

            TimeframeCandles? data = options.Timeframes != null
                ? options.Timeframes.GetValueOrDefault(name)
                : options.TimeframeProvider?.Invoke(name);

            StockSeries? series = null;

            if (data != null)
            {
                if (!double.IsFinite(data.Length) || data.Length <= 0)
                    throw new ArgumentException($"The length of the timeframe \"{name}\" must be a positive number of milliseconds.");

                series = new StockSeries(data.Candles, parent.Executor, name, parent.Instrument, data.Length, this);

                for (int i = 0; i < series.Count; i++)
                {
                    if (series.Candles[i].Time == null)
                        throw new ArgumentException($"Candle {i} of the timeframe \"{name}\" has no time.");
                }
            }

            _loaded[name] = series;
            return series;
        }

        public void Register(string name, StockSeries series) => _loaded[name] = series;

        /// <summary>
        /// Forgets the loaded timeframes: the host application gives their candles again (new candles closed).
        /// </summary>
        public void Reload() => _loaded.Clear();
    }

    /// <summary>
    /// Candles of one chart and the values derived from them, shared by a Stock Context and every child Indicator
    /// Context created by <c>LOAD_INDICATOR</c> or <c>LOAD_PATTERN</c>. Also stores the computed indicator and pattern
    /// series, which are deterministic (docs/standards/stock/functions/LOAD_INDICATOR.md, "Performance"), and the
    /// candles of the other timeframes.
    /// </summary>
    internal sealed class StockSeries
    {
        private readonly Dictionary<string, IValue> _stored = new(StringComparer.Ordinal);
        private ConditionalWeakTable<IValue[], string> _sourceKeys = new();
        private readonly Dictionary<StockSeries, int[]> _alignments = new(ReferenceEqualityComparer.Instance);
        private readonly Timeframes _timeframes;

        public IReadOnlyList<Candle> Candles { get; private set; } = [];
        public int Count => Candles.Count;

        /// <summary>
        /// Executor of the indicator and pattern formulas.
        /// </summary>
        public Executor Executor { get; }

        /// <summary>The <c>TIMEFRAME</c> constant, or <c>null</c>.</summary>
        public string? Timeframe { get; }

        /// <summary>The <c>INSTRUMENT</c> constant, or <c>null</c>.</summary>
        public string? Instrument { get; }

        /// <summary>Length of a candle, in milliseconds, or <c>null</c>.</summary>
        public double? TimeframeLength { get; }

        public IValue CandlesValue { get; private set; } = null!;
        public IValue Open { get; private set; } = null!;
        public IValue High { get; private set; } = null!;
        public IValue Low { get; private set; } = null!;
        public IValue Close { get; private set; } = null!;
        public IValue Volume { get; private set; } = null!;
        public IValue Time { get; private set; } = null!;

        /// <exception cref="ArgumentException">A candle breaks the rules of <c>Stock.Candle</c>.</exception>
        public StockSeries(IEnumerable<Candle> candles, Executor? executor)
            : this(candles, new StockContextOptions { Executor = executor }) { }

        /// <exception cref="ArgumentException">
        /// A candle breaks the rules of <c>Stock.Candle</c>, or other timeframes are given without the length of the main
        /// timeframe.
        /// </exception>
        public StockSeries(IEnumerable<Candle> candles, StockContextOptions options)
            : this(candles, options.Executor, options.Timeframe, options.Instrument, options.TimeframeLength, null)
        {
            if (options.HasTimeframes && TimeframeLength == null)
                throw new ArgumentException("The length of the main timeframe (TimeframeLength) is needed with other timeframes.", nameof(options));

            if (TimeframeLength is double length && (!double.IsFinite(length) || length <= 0))
                throw new ArgumentException("The length of the main timeframe must be a positive number of milliseconds.", nameof(options));

            _timeframes = new Timeframes(options);

            if (Timeframe != null)
                _timeframes.Register(Timeframe, this);
        }

        internal StockSeries(IEnumerable<Candle> candles, Executor? executor, string? timeframe, string? instrument,
            double? timeframeLength, Timeframes? shared)
        {
            Executor = executor ?? Executor.Create();
            Timeframe = timeframe;
            Instrument = instrument;
            TimeframeLength = timeframeLength;
            _timeframes = shared!;
            SetCandles(candles.ToArray());
        }

        private void SetCandles(Candle[] candles)
        {
            for (int i = 0; i < candles.Length; i++)
            {
                if (!candles[i].IsValid())
                    throw new ArgumentException($"Candle {i} does not meet the requirements of {Candle.TypeName}.", nameof(candles));
            }

            Candles = candles;
            CandlesValue = Value.Array(Candles.Select(c => c.ToValue()), "CANDLES");
            Open = Numbers(c => c.Open, "OPEN");
            High = Numbers(c => c.High, "HIGH");
            Low = Numbers(c => c.Low, "LOW");
            Close = Numbers(c => c.Close, "CLOSE");
            Volume = Numbers(c => c.Volume, "VOLUME");
            Time = Numbers(c => c.Time, "TIME");
        }

        /// <summary>
        /// Adds candles to the end of the chart. The stored series and the other timeframes are computed again, because
        /// their lengths and their closed candles change.
        /// </summary>
        /// <exception cref="ArgumentException">A candle breaks the rules of <c>Stock.Candle</c>.</exception>
        public void Append(IEnumerable<Candle> candles)
        {
            SetCandles([.. Candles, .. candles]);
            _stored.Clear();
            _alignments.Clear();
            _sourceKeys = new();
            _timeframes.Reload();

            if (Timeframe != null)
                _timeframes.Register(Timeframe, this);
        }

        private Value Numbers(Func<Candle, double?> select, string name)
            => Value.Array(Candles.Select(c => select(c) is double d
                ? (IValue)Value.Number(d, StringConverter.NumberToString(d))
                : Value.Null("null")), name);

        public bool TryGetStored(string key, out IValue? series) => _stored.TryGetValue(key, out series);

        public void Store(string key, IValue series) => _stored[key] = series;

        /// <summary>
        /// The series of the candles of a timeframe, or <c>null</c> if the host application does not provide it.
        /// </summary>
        public StockSeries? OtherTimeframe(string name) => name == Timeframe ? this : _timeframes.Get(name, this);

        /// <summary>
        /// For every candle <c>k</c> of this series, the position of the last candle of <paramref name="other"/> closed at
        /// the candle <c>k</c> (docs/standards/stock/README.md, "Closed candles"), or <c>-1</c> if there is none. A candle
        /// of <paramref name="other"/> with the time <c>t</c> is closed at <c>k</c> if <c>t + L &lt;= TIME[k] + L₀</c>.
        /// </summary>
        public int[] Alignment(StockSeries other)
        {
            if (_alignments.TryGetValue(other, out int[]? known))
                return known;

            known = new int[Count];

            if (ReferenceEquals(other, this))
            {
                for (int k = 0; k < known.Length; k++)
                    known[k] = k;
            }
            else
            {
                double mainLength = TimeframeLength
                    ?? throw new InvalidOperationException("The length of the main timeframe (TimeframeLength) is needed with other timeframes.");
                double length = other.TimeframeLength!.Value;
                int j = -1;

                for (int k = 0; k < known.Length; k++)
                {
                    if (Candles[k].Time is not double time)
                    {
                        known[k] = -1;
                        continue;
                    }

                    while (j + 1 < other.Count && other.Candles[j + 1].Time!.Value + length <= time + mainLength)
                        j++;

                    known[k] = j;
                }
            }

            _alignments[other] = known;
            return known;
        }

        private readonly ConditionalWeakTable<IValue[], object> _validSources = new();

        /// <summary>
        /// Whether every element of a source is a Number or <c>null</c>. The check runs once for every array
        /// (<c>CLOSE</c> never), not in every iteration of the caller.
        /// </summary>
        public bool IsValidSource(IValue[] source)
        {
            if (ReferenceEquals(source, Close.Raw) || _validSources.TryGetValue(source, out _))
                return true;

            if (source.Any(e => e.Type.Base != Types.NumberName && e.Type.Base != Types.NullName))
                return false;

            _validSources.AddOrUpdate(source, source);
            return true;
        }

        /// <summary>
        /// Key of an indicator source: <c>CLOSE</c> itself, or a key of the elements. Sources with the same elements get the
        /// same key, also when the formula builds a new array in every iteration (e.g. with <c>MAP</c>).
        /// </summary>
        /// <remarks>
        /// The elements are compared as numbers instead of being serialized to text, which made the key cost more than
        /// the rest of a <c>LOAD_INDICATOR</c> call.
        /// </remarks>
        public string SourceKey(IValue[] source)
        {
            if (ReferenceEquals(source, Close.Raw))
                return "CLOSE";

            return _sourceKeys.GetValue(source, ContentKey);
        }

        private readonly Dictionary<int, List<(double?[] Elements, string Key)>> _contents = [];
        private int _sourceCount;

        /// <summary>
        /// Elements of a source: Numbers and <c>null</c> values (checked by the caller).
        /// </summary>
        private string ContentKey(IValue[] source)
        {
            var elements = new double?[source.Length];
            var hash = new HashCode();

            for (int i = 0; i < source.Length; i++)
            {
                elements[i] = source[i].Raw as double?;
                hash.Add(elements[i]);
            }

            int code = hash.ToHashCode();

            if (!_contents.TryGetValue(code, out var candidates))
                _contents[code] = candidates = [];

            foreach (var (known, key) in candidates)
            {
                if (known.SequenceEqual(elements))
                    return key;
            }

            string created = $"SOURCE#{_sourceCount++}";
            candidates.Add((elements, created));
            return created;
        }
    }
}
