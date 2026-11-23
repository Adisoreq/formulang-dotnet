using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Processing;
using FormuLang.NET.Core.Structures;

namespace FormuLang.NET.Core.Processing
{
    /// <summary>
    /// Executes a parsed formula frame by frame in a <see cref="TimeContext"/>. Created by
    /// <see cref="Executor.CreateFrameRunner"/>; the host application decides when the next frame starts and when the
    /// frames stop.
    /// </summary>
    public sealed class FrameRunner
    {
        private readonly Executor _executor;
        private readonly IParsingResult? _parsing;
        private IValue? _previous;
        private bool _started;

        public TimeContext Context { get; }

        /// <summary>
        /// The failed result of the validation, clean-up, tokenization or parsing, or <c>null</c> if the formula is ready.
        /// Every frame returns this result when it is set.
        /// </summary>
        public ExecutionResult? PreparationError { get; }

        /// <summary>
        /// Number of executed frames.
        /// </summary>
        public int FrameCount { get; private set; }

        internal FrameRunner(Executor executor, TimeContext context, IParsingResult? parsing, ExecutionResult? preparationError)
        {
            _executor = executor;
            Context = context;
            _parsing = parsing;
            PreparationError = preparationError;
        }

        /// <summary>
        /// Executes the next frame.
        /// </summary>
        /// <param name="timeSeconds">Time from the start of the first frame, in seconds. Never decreases.</param>
        /// <param name="deltaSeconds">
        /// Time from the start of the previous frame, in seconds; <c>null</c> to compute it from the previous frame
        /// (<c>0</c> in the first frame).
        /// </param>
        /// <param name="now"><c>NOW</c> of the frame in milliseconds, or <c>null</c> to read the clock of the environment.</param>
        public ExecutionResult NextFrame(double timeSeconds, double? deltaSeconds = null, double? now = null)
        {
            if (PreparationError != null)
                return PreparationError;

            double delta = deltaSeconds ?? (_started ? timeSeconds - Context.TimeSeconds : 0);

            Context.BeginFrame(FrameCount, timeSeconds, Math.Max(delta, 0), _previous, now);
            _started = true;

            ExecutionResult result = _executor.EvaluateFrame(_parsing!, Context);
            FrameCount++;
            _previous = result.Value;

            return result;
        }

        /// <summary>
        /// Starts again from the first frame: <c>PREV</c> is <c>null</c> and <c>FRAME</c> is the first index.
        /// </summary>
        public void Reset()
        {
            FrameCount = 0;
            _previous = null;
            _started = false;
        }
    }
}
