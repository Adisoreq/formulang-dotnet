using FormuLang.NET.Abstractions.Primitives;

namespace FormuLang.NET.Abstractions.Processing
{
    /// <summary>
    /// Result of the metadata reading (docs/core/metadata.md, "Metadata reading", since <c>core-1.2</c>).
    /// </summary>
    public interface IMetadataResult : IOperationResult
    {
        /// <summary>
        /// The metadata Object; <c>{ }</c> for a formula without a metadata block, or after an Error.
        /// </summary>
        public IValue Metadata { get; }

        /// <summary>
        /// The formula with the metadata block replaced by white space, so the positions of the rest do not change.
        /// </summary>
        public string Formula { get; }
    }

    /// <summary>
    /// A <b>prepared formula</b> (docs/semantics/execution.md, "Execution modes", since <c>core-1.2</c>): the result of
    /// the preparation (validation, metadata reading, clean-up, tokenization and parsing). A hot execution evaluates it
    /// again without preparing the formula.
    /// </summary>
    public interface IPreparedFormula : IParsingResult
    {
        /// <summary>
        /// The formula text that was prepared.
        /// </summary>
        public string Formula { get; }

        /// <summary>
        /// The metadata Object of the formula (<c>{ }</c> without a metadata block).
        /// </summary>
        public IValue Metadata { get; }

        /// <summary>
        /// Warnings of the preparation, in the order of their positions.
        /// </summary>
        public IReadOnlyList<IWarning> Warnings { get; }
    }
}
