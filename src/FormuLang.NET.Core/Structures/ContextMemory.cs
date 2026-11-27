using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;

namespace FormuLang.NET.Core.Structures
{
    /// <summary>
    /// Memory of an Iteration or Time Context (docs/core/context.md, "Memory", since <c>core-1.2</c>).
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item><b>Snapshot</b>: during an iteration the stored values do not change; the writes of <c>STORE</c> and
    /// <c>CLEAR</c> are collected and applied by <see cref="Commit"/> after the iteration, in the order in which they were
    /// executed.</item>
    /// <item><b>Selected paths only</b>: the evaluator discards the writes executed in a branch of <c>IF</c> that is not
    /// selected, or in an argument of <c>AND</c> / <c>OR</c> that does not decide the result.</item>
    /// <item><b>Errors</b>: the executor applies the writes only if the iteration did not result in an Error
    /// (<see cref="Rollback"/> otherwise).</item>
    /// </list>
    /// The host application may read the stored values (<see cref="Entries"/>) and restore them (<see cref="Load"/>), e.g.
    /// to resume the iterations later.
    /// </remarks>
    public sealed class ContextMemory : IMemory
    {
        private sealed class PendingWrite(string? key, IValue? value)
        {
            public string? Key { get; } = key;
            public IValue? Value { get; } = value;
            public bool Discarded { get; set; }
        }

        private readonly Dictionary<string, IValue> _values = new(StringComparer.Ordinal);
        private readonly List<string> _order = [];
        private List<PendingWrite> _pending = [];

        public IReadOnlyList<string> Keys => _order.ToArray();

        /// <summary>
        /// Number of stored keys.
        /// </summary>
        public int Count => _values.Count;

        public IValue? Recall(string key) => _values.TryGetValue(key, out IValue? value) ? value : null;

        public void Store(string key, IValue value) => _pending.Add(new PendingWrite(key, value));

        public void Clear(string? key) => _pending.Add(new PendingWrite(key, null));

        /// <summary>
        /// The stored keys and values, in the order of their first write.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, IValue>> Entries()
            => _order.Select(key => new KeyValuePair<string, IValue>(key, _values[key])).ToArray();

        /// <summary>
        /// Replaces the stored values (e.g. a state kept by the host application) and drops the collected writes.
        /// </summary>
        public void Load(IEnumerable<KeyValuePair<string, IValue>> entries)
        {
            Reset();

            foreach (var (key, value) in entries)
                Set(key, value);
        }

        /// <summary>
        /// Number of the writes collected in the current iteration, kept or discarded.
        /// </summary>
        internal int PendingCount => _pending.Count;

        /// <summary>
        /// Discards the collected writes from <paramref name="from"/> (inclusive) to <paramref name="to"/> (exclusive).
        /// </summary>
        internal void Discard(int from, int to)
        {
            for (int i = from; i < to && i < _pending.Count; i++)
                _pending[i].Discarded = true;
        }

        /// <summary>
        /// Applies the writes of the iteration that are not discarded, in order; the last write of a key wins.
        /// </summary>
        public void Commit()
        {
            foreach (PendingWrite write in _pending)
            {
                if (write.Discarded)
                    continue;

                if (write.Value != null)
                    Set(write.Key!, write.Value);
                else if (write.Key == null)
                {
                    _values.Clear();
                    _order.Clear();
                }
                else if (_values.Remove(write.Key))
                    _order.Remove(write.Key);
            }

            _pending = [];
        }

        /// <summary>
        /// Drops the writes of the iteration (the iteration resulted in an Error).
        /// </summary>
        public void Rollback() => _pending = [];

        /// <summary>
        /// Empties the memory, as before the first iteration.
        /// </summary>
        public void Reset()
        {
            _values.Clear();
            _order.Clear();
            _pending = [];
        }

        private void Set(string key, IValue value)
        {
            if (!_values.ContainsKey(key))
                _order.Add(key);

            _values[key] = value;
        }
    }
}
