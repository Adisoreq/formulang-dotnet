using FormuLang.NET.Abstractions.Primitives;

namespace FormuLang.NET.Abstractions.Structures
{
    /// <summary>
    /// Memory of an Iteration or Time Context (docs/core/context.md, "Memory", since <c>core-1.2</c>): a map from Text
    /// keys to values that keeps a state between iterations. A formula uses it with <c>STORE</c>, <c>RECALL</c> and
    /// <c>CLEAR</c>.
    /// </summary>
    /// <remarks>
    /// The writes of an iteration are collected and applied after it, so <see cref="Recall"/> gives the values as they
    /// were at the start of the iteration.
    /// </remarks>
    public interface IMemory
    {
        /// <summary>
        /// Stored keys, in the order of their first write.
        /// </summary>
        public IReadOnlyList<string> Keys { get; }

        /// <summary>
        /// The stored value, as it was at the start of the current iteration; <c>null</c> if the key is not stored.
        /// </summary>
        public IValue? Recall(string key);

        /// <summary>
        /// Collects a write, applied after the current iteration.
        /// </summary>
        public void Store(string key, IValue value);

        /// <summary>
        /// Collects the removal of a key, or of every key when <paramref name="key"/> is <c>null</c>, applied after the
        /// current iteration.
        /// </summary>
        public void Clear(string? key);
    }
}
