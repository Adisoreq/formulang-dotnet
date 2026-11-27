using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Standards;
using FormuLang.NET.Core.Utils;

namespace FormuLang.NET.Core.Structures
{
    /// <summary>
    /// Core <b>Time Context</b> (docs/core/context.md, "Time Context"): the host application executes the same formula
    /// repeatedly as time passes, once for every <b>frame</b> (an animation frame, a simulation step, a real-time
    /// measurement). Constants: <c>TIME_S</c>, <c>TIME_MS</c>, <c>DTIME_S</c>, <c>DTIME_MS</c>, <c>FRAME</c> (follows
    /// <c>@array_indexing</c>) and <c>PREV</c> (result of the previous frame).
    /// </summary>
    /// <remarks>
    /// Run the frames with <see cref="Processing.Executor.CreateFrameRunner(string, TimeContext)"/> (the formula is parsed once), or call
    /// <see cref="BeginFrame"/> before every execution. Every frame starts with no context variables and with the
    /// environment variables of the environment; the only state passed from one frame to the next is <c>PREV</c> and
    /// the <see cref="Memory"/> (since <c>core-1.2</c>). <c>NOW</c> is set again for every frame.
    /// </remarks>
    public class TimeContext : Context
    {
        public const string TimeSecondsConstant = "TIME_S";
        public const string TimeMillisecondsConstant = "TIME_MS";
        public const string DeltaSecondsConstant = "DTIME_S";
        public const string DeltaMillisecondsConstant = "DTIME_MS";
        public const string FrameConstant = "FRAME";
        public const string PreviousConstant = "PREV";

        /// <summary>
        /// 0-based index of the current frame. <c>FRAME</c> is this value plus <c>@array_indexing</c>.
        /// </summary>
        public int Frame { get; private set; }

        /// <summary>
        /// Time from the start of the first frame to the start of the current frame, in seconds (<c>TIME_S</c>).
        /// </summary>
        public double TimeSeconds { get; private set; }

        /// <summary>
        /// Time from the start of the previous frame to the start of the current frame, in seconds (<c>DTIME_S</c>).
        /// </summary>
        public double DeltaSeconds { get; private set; }

        /// <summary>
        /// Result of the previous frame (<c>PREV</c>); <c>null</c> in the first frame.
        /// </summary>
        public IValue Previous { get; private set; } = Value.Null(PreviousConstant);

        private static readonly IReadOnlyList<string> TimeTypeNames = ["Time", "Default"];
        private readonly ContextMemory _memory = new();

        protected TimeContext(IEnvironment environment)
            : base(environment, ContextType.Persistent) { }

        /// <summary>
        /// The memory of the context (<c>STORE</c>, <c>RECALL</c>, <c>CLEAR</c>); emptied when the first frame begins.
        /// </summary>
        public override ContextMemory Memory => _memory;

        public override IReadOnlyList<string> ContextTypeNames => TimeTypeNames;

        /// <summary>
        /// Creates a Time Context positioned at the first frame (<c>TIME_S</c> = 0).
        /// </summary>
        public static TimeContext Create(IEnvironment environment)
        {
            var context = new TimeContext(environment);
            context.PreExecute();
            return context;
        }

        /// <summary>
        /// <c>NOW</c> is set by <see cref="BeginFrame"/>, once for every frame.
        /// </summary>
        protected override bool RefreshesNowOnPreExecute => false;

        protected override void LoadContextData()
        {
            SetTimeConstants();
            SetContainer(new ComputedConstant(FrameConstant, Types.Number,
                () => Value.Number(Frame + FunctionHelpers.ArrayIndexing(this), FrameConstant)));
            SetConstant(PreviousConstant, Previous);
        }

        /// <summary>
        /// Prepares the frame with the 0-based <paramref name="frame"/> index: removes the context variables and the
        /// overridden environment variables, sets the time constants, <c>PREV</c> and <c>NOW</c>, and runs the
        /// pre-execution.
        /// </summary>
        /// <param name="timeSeconds">Total time from the start of the first frame, in seconds. Never decreases.</param>
        /// <param name="deltaSeconds">Time from the start of the previous frame, in seconds. Never negative.</param>
        /// <param name="previous">Result of the previous frame; <c>null</c> (or an Error) is stored as <c>null</c>.</param>
        /// <param name="now">
        /// <c>NOW</c> of the frame (Unix time in milliseconds, UTC), or <c>null</c> to read the clock of the environment.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">A negative frame, time or delta, or a time that decreases.</exception>
        public void BeginFrame(int frame, double timeSeconds, double deltaSeconds, IValue? previous, double? now = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(frame);
            ArgumentOutOfRangeException.ThrowIfNegative(timeSeconds);
            ArgumentOutOfRangeException.ThrowIfNegative(deltaSeconds);

            if (frame > 0 && timeSeconds < TimeSeconds)
                throw new ArgumentOutOfRangeException(nameof(timeSeconds), "The time of a frame never decreases.");

            EnsureContextData(); // loads the context constants before the first frame
            ClearVariables();

            if (frame == 0)
                _memory.Reset();

            Frame = frame;
            TimeSeconds = timeSeconds;
            DeltaSeconds = deltaSeconds;
            Previous = previous == null || previous.Type.Base == Types.ErrorName ? Value.Null(PreviousConstant) : previous;

            SetTimeConstants();
            SetConstant(PreviousConstant, Previous);
            RefreshNow(now is double ms ? Structures.Environment.NowValue(ms) : null);

            PreExecute();
        }

        private void SetTimeConstants()
        {
            SetConstant(TimeSecondsConstant, Number(TimeSeconds, TimeSecondsConstant));
            SetConstant(TimeMillisecondsConstant, Number(TimeSeconds * 1000, TimeMillisecondsConstant));
            SetConstant(DeltaSecondsConstant, Number(DeltaSeconds, DeltaSecondsConstant));
            SetConstant(DeltaMillisecondsConstant, Number(DeltaSeconds * 1000, DeltaMillisecondsConstant));
        }

        private static IValue Number(double value, string name) => Value.Number(value, name);
    }
}
