using FormuLang.NET.Core.Processing;

namespace WebPreview.Model
{
    /// <param name="Frame">0-based index of the frame (<c>FRAME</c> is this value plus <c>@array_indexing</c>).</param>
    /// <param name="TimeSeconds"><c>TIME_S</c> of the frame.</param>
    /// <param name="DeltaSeconds"><c>DTIME_S</c> of the frame.</param>
    public sealed record FrameRecord(int Frame, double TimeSeconds, double DeltaSeconds, ExecutionResult Result);

    /// <summary>
    /// Frames of a formula in a Time Context (docs/core/context.md, "Time Context"). The formula is parsed once; every
    /// <see cref="Advance"/> executes the next frame. The preview decides when the frames start and stop: it plays them
    /// with a timer and stops at the first frame that results in an Error.
    /// </summary>
    public sealed class TimeSession
    {
        /// <summary>Number of the last frames kept for the output.</summary>
        public const int HistoryLimit = 240;

        private readonly FrameRunner _runner;
        private readonly List<FrameRecord> _frames = [];

        public TimeSession(FrameRunner runner)
        {
            _runner = runner;
        }

        /// <summary>The failed result of the validation, clean-up, tokenization or parsing; <c>null</c> if the formula is ready.</summary>
        public ExecutionResult? PreparationError => _runner.PreparationError;

        /// <summary>The last <see cref="HistoryLimit"/> frames, oldest first.</summary>
        public IReadOnlyList<FrameRecord> Frames => _frames;

        public FrameRecord? Last => _frames.Count > 0 ? _frames[^1] : null;

        /// <summary><c>TIME_S</c> of the last frame; 0 before the first one.</summary>
        public double TimeSeconds => Last?.TimeSeconds ?? 0;

        /// <summary>Number of executed frames (also those no longer in <see cref="Frames"/>).</summary>
        public int FrameCount => _runner.FrameCount;

        /// <summary>The formula cannot be executed, or the last frame resulted in an Error.</summary>
        public bool Failed => PreparationError != null || Last?.Result.Successful == false;

        /// <summary>
        /// Executes the next frame, <paramref name="deltaSeconds"/> after the previous one. The first frame starts at
        /// <c>TIME_S</c> = 0 with <c>DTIME_S</c> = 0, whatever the delta.
        /// </summary>
        public FrameRecord Advance(double deltaSeconds)
        {
            bool first = _runner.FrameCount == 0;
            double delta = first ? 0 : Math.Max(deltaSeconds, 0);
            double time = first ? 0 : TimeSeconds + delta;

            int index = _runner.FrameCount;
            ExecutionResult result = _runner.NextFrame(time, delta);
            var frame = new FrameRecord(index, time, delta, result);

            _frames.Add(frame);
            if (_frames.Count > HistoryLimit)
                _frames.RemoveAt(0);

            return frame;
        }

        /// <summary>Starts again from the first frame.</summary>
        public void Reset()
        {
            _runner.Reset();
            _frames.Clear();
        }
    }
}
