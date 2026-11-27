using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Processing;

namespace FormuLang.NET.Core.Processing
{
    /// <summary>
    /// Tokens around the beginning of a sequence, for the warnings of the preparation.
    /// </summary>
    /// <param name="First">The first token of the sequence.</param>
    /// <param name="Separator">The separator before the sequence (<c>\n</c> or <c>;</c>); <c>null</c> for the first sequence.</param>
    /// <param name="PreviousLast">The last token of the previous sequence.</param>
    /// <param name="Line">Line of the first token.</param>
    internal sealed record SequenceStart(IToken First, IToken? Separator, IToken? PreviousLast, int Line);

    public sealed class ParsingResult : IParsingResult
    {
        public IReadOnlyList<Sequence> Sequences { get; }
        public IReadOnlyDictionary<string, IExpressionNode> PrimitiveMap { get; }
        public IError Error { get; }

        IReadOnlyList<ISequence> IParsingResult.Sequences => Sequences;

        /// <summary>
        /// The beginnings of the sequences, in their order; <c>null</c> if the parser does not record them.
        /// </summary>
        internal IReadOnlyList<SequenceStart>? Starts { get; }

        public ParsingResult(IReadOnlyList<Sequence> sequences, IError error)
            : this(sequences, error, null) { }

        internal ParsingResult(IReadOnlyList<Sequence> sequences, IError error, IReadOnlyList<SequenceStart>? starts)
        {
            Sequences = sequences;
            PrimitiveMap = Sequence.BuildPrimitiveMap(sequences);
            Error = error;
            Starts = starts;
        }

        public static ParsingResult Failed(IError error) => new([], error);
    }
}
