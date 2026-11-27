namespace FormuLang.NET.Abstractions.Processing
{
    /// <summary>
    /// A message about a formula that is valid, but probably not what the author meant (docs/standards/core/semantics/warnings.md,
    /// since <c>core-1.2</c>). A warning never changes the result of a formula.
    /// </summary>
    public interface IWarning
    {
        /// <summary>
        /// Code of the warning; the Core standard defines the <see cref="WarningCode"/>s.
        /// </summary>
        public string Code { get; }

        public string Message { get; }

        /// <summary>
        /// Line of the original formula text, from 1.
        /// </summary>
        public int Line { get; }

        /// <summary>
        /// Column of the original formula text (in Unicode code points), from 1.
        /// </summary>
        public int Column { get; }
    }

    /// <summary>
    /// Codes of the warnings of the Core standard (docs/standards/core/semantics/warnings.md).
    /// </summary>
    public static class WarningCode
    {
        /// <summary>A sequence that is not the last one computes a value that is discarded.</summary>
        public const string UnusedValue = "UNUSED_VALUE";

        /// <summary>A sequence that starts with <c>-</c> or <c>!</c> on the line after another sequence.</summary>
        public const string ContinuedLine = "CONTINUED_LINE";

        /// <summary>A known key of the metadata has a value of a wrong type.</summary>
        public const string MetadataType = "METADATA_TYPE";

        /// <summary>The <c>context</c> of the metadata is not the context type of the execution, nor one of its base types.</summary>
        public const string MetadataContext = "METADATA_CONTEXT";
    }
}
