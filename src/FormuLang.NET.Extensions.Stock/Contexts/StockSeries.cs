using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Core.Utils;
using System.Runtime.CompilerServices;

namespace FormuLang.NET.Extensions.Stock.Contexts
{
    /// <summary>
    /// Candles of one chart and the values derived from them, shared by a Stock Context and every child Indicator
    /// Context created by <c>LOAD_INDICATOR</c> or <c>LOAD_PATTERN</c>. Also stores the computed indicator and pattern series, which are deterministic
    /// (docs/standards/stock/functions/LOAD_INDICATOR.md, "Performance").
    /// </summary>
    internal sealed class StockSeries
    {
        private readonly Dictionary<string, IValue> _stored = new(StringComparer.Ordinal);
        private readonly ConditionalWeakTable<IValue[], string> _sourceKeys = new();

        public IReadOnlyList<Candle> Candles { get; }
        public int Count => Candles.Count;

        /// <summary>
        /// Executor of the indicator and pattern formulas.
        /// </summary>
        public Executor Executor { get; }

        public IValue CandlesValue { get; }
        public IValue Open { get; }
        public IValue High { get; }
        public IValue Low { get; }
        public IValue Close { get; }
        public IValue Volume { get; }
        public IValue Time { get; }

        /// <exception cref="ArgumentException">A candle breaks the rules of <c>Stock.Candle</c>.</exception>
        public StockSeries(IEnumerable<Candle> candles, Executor? executor)
        {
            Candles = candles.ToArray();
            Executor = executor ?? Executor.Create();

            for (int i = 0; i < Candles.Count; i++)
            {
                if (!Candles[i].IsValid())
                    throw new ArgumentException($"Candle {i} does not meet the requirements of {Candle.TypeName}.", nameof(candles));
            }

            CandlesValue = Value.Array(Candles.Select(c => c.ToValue()), "CANDLES");
            Open = Numbers(c => c.Open, "OPEN");
            High = Numbers(c => c.High, "HIGH");
            Low = Numbers(c => c.Low, "LOW");
            Close = Numbers(c => c.Close, "CLOSE");
            Volume = Numbers(c => c.Volume, "VOLUME");
            Time = Numbers(c => c.Time, "TIME");
        }

        private Value Numbers(Func<Candle, double?> select, string name)
            => Value.Array(Candles.Select(c => select(c) is double d
                ? (IValue)Value.Number(d, StringConverter.NumberToString(d))
                : Value.Null("null")), name);

        public bool TryGetStored(string key, out IValue? series) => _stored.TryGetValue(key, out series);

        public void Store(string key, IValue series) => _stored[key] = series;

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
