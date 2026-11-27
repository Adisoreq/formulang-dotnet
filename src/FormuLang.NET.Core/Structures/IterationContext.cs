using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Standards;

namespace FormuLang.NET.Core.Structures
{
    /// <summary>
    /// Core <b>Iteration Context</b> (docs/core/context.md, "Iteration Context"): the host application executes the same
    /// formula once for every element of a collection. Constants: <c>I</c> (index of the current iteration, follows
    /// <c>@array_indexing</c>), <c>N</c> (number of iterations), <c>PREV</c> (result of the previous iteration) and,
    /// since <c>core-1.2</c>, <c>HISTORY</c> (results of all the previous iterations), and a <see cref="Memory"/>.
    /// </summary>
    /// <remarks>
    /// Run the iterations with <see cref="Processing.Executor.ExecuteIterations(string, IterationContext, bool)"/>, or call
    /// <see cref="BeginIteration"/> before every execution, and continue them on a longer collection with
    /// <see cref="Processing.Executor.ResumeIterations(string, IterationContext, bool)"/>. Every iteration starts with no
    /// context variables and with the environment variables of the environment; the state passed from one iteration to
    /// the next is <c>PREV</c>, <c>HISTORY</c> and the memory.
    /// </remarks>
    public class IterationContext : Context
    {
        public const string IterationConstant = "I";
        public const string CountConstant = "N";
        public const string PreviousConstant = "PREV";
        public const string HistoryConstant = "HISTORY";

        private static readonly IReadOnlyList<string> IterationTypeNames = ["Iteration", "Default"];

        // Results of the previous iterations (an Error result is null); HISTORY is built from them when it is read
        private readonly List<IValue> _history = [];
        private IValue? _historyValue;
        private readonly ContextMemory _memory = new();
        private ReplaceableConstant? _previous;

        /// <summary>
        /// Number of iterations (<c>N</c>).
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// 0-based index of the current iteration. <c>I</c> is this value plus <c>@array_indexing</c>.
        /// </summary>
        public int Iteration { get; private set; }

        /// <summary>
        /// Result of the previous iteration (<c>PREV</c>); <c>null</c> in the first iteration.
        /// </summary>
        public IValue Previous { get; private set; } = Value.Null(PreviousConstant);

        /// <summary>
        /// Results of the previous iterations (<c>HISTORY</c>); an iteration that resulted in an Error is <c>null</c>.
        /// </summary>
        public IReadOnlyList<IValue> History => _history;

        /// <summary>
        /// The memory of the context (<c>STORE</c>, <c>RECALL</c>, <c>CLEAR</c>); emptied when the first iteration begins.
        /// </summary>
        public override ContextMemory Memory => _memory;

        public override IReadOnlyList<string> ContextTypeNames => IterationTypeNames;

        /// <summary>
        /// Number of iterations executed by the executor (<see cref="Processing.Executor.ExecuteIterations(string, IterationContext, bool)"/>
        /// or <see cref="Processing.Executor.ResumeIterations(string, IterationContext, bool)"/>); the next resumed
        /// iteration has this index.
        /// </summary>
        public int CompletedIterations { get; private set; }

        /// <summary>
        /// Result of the last iteration executed by the executor, or <c>null</c> before the first one.
        /// </summary>
        public IValue? LastResult { get; private set; }

        protected IterationContext(IEnvironment environment, int count)
            : base(environment, ContextType.Persistent)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            Count = count;
        }

        /// <summary>
        /// Creates an Iteration Context for <paramref name="count"/> iterations, positioned at the first one.
        /// </summary>
        public static IterationContext Create(IEnvironment environment, int count)
        {
            var context = new IterationContext(environment, count);
            context.PreExecute();
            return context;
        }

        /// <summary>
        /// Increases the number of iterations, so the iterations can be resumed on a longer collection
        /// (docs/core/context.md, "Resumed iterations"). The state of the executed iterations is kept.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The count is lower than the current one.</exception>
        public virtual void Extend(int count) => Resize(count);

        /// <summary>
        /// Sets the number of iterations (<c>N</c>); for derived context types that grow with their collection.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The count is lower than the current one.</exception>
        protected void Resize(int count)
        {
            if (count < Count)
                throw new ArgumentOutOfRangeException(nameof(count), "The number of iterations can only grow.");

            Count = count;

            if (HasKey(CountConstant))
                SetConstant(CountConstant, Value.Number(count, CountConstant));
        }

        /// <summary>
        /// Records the result of the current iteration, executed by the executor.
        /// </summary>
        internal void CompleteIteration(IValue result)
        {
            CompletedIterations = Iteration + 1;
            LastResult = result;
        }

        /// <summary>
        /// The state that is passed to the next iterations (docs/core/context.md, "Resumed iterations"): the number of
        /// executed iterations, the last result, <c>HISTORY</c> and the memory. A host application may keep it and
        /// <see cref="RestoreState"/> it in a new context over the same collection, to resume the iterations later.
        /// </summary>
        public IterationState SaveState() => new(CompletedIterations, LastResult, _history.ToArray(), _memory.Entries());

        /// <summary>
        /// Restores a state saved with <see cref="SaveState"/>; the next
        /// <see cref="Processing.Executor.ResumeIterations(string, IterationContext, bool)"/> continues after its last
        /// executed iteration.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The state has more executed iterations than the context, or a <c>HISTORY</c> that does not belong to its
        /// executed iterations.
        /// </exception>
        public void RestoreState(IterationState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            int completed = state.CompletedIterations;

            if (completed < 0 || completed > Count)
                throw new ArgumentException($"The state has {completed} executed iterations, but the context has {Count}.", nameof(state));

            if (completed > 0 && state.History.Count != completed - 1)
                throw new ArgumentException("HISTORY of the state must have one element for every executed iteration but the last.", nameof(state));

            EnsureContextData();
            _history.Clear();
            _history.AddRange(state.History);
            _historyValue = null;
            _memory.Load(state.Memory);
            CompletedIterations = completed;
            LastResult = state.LastResult;
            Iteration = Math.Max(completed - 1, 0);
        }

        /// <summary>
        /// <c>NOW</c> is set once before the first iteration and is the same in all iterations.
        /// </summary>
        protected override bool RefreshesNowOnPreExecute => false;

        protected override void LoadContextData()
        {
            SetConstant(CountConstant, Value.Number(Count, CountConstant));
            SetContainer(new ComputedConstant(IterationConstant, Types.Number,
                () => Value.Number(Iteration + FunctionHelpers.ArrayIndexing(this), IterationConstant)));
            SetContainer(_previous = new ReplaceableConstant(PreviousConstant, Previous));
            SetContainer(new ComputedConstant(HistoryConstant, Types.Array,
                () => _historyValue ??= Value.Array(_history.ToArray(), HistoryConstant)));
        }

        /// <summary>
        /// Prepares the iteration with the 0-based <paramref name="iteration"/> index: removes the context variables and
        /// the overridden environment variables, sets <c>PREV</c> and <c>HISTORY</c>, and runs the pre-execution. The first
        /// iteration starts with an empty <c>HISTORY</c> and an empty memory.
        /// </summary>
        /// <param name="previous">Result of the previous iteration; <c>null</c> (or an Error) is stored as <c>null</c>.</param>
        public void BeginIteration(int iteration, IValue? previous)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(iteration);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(iteration, Math.Max(Count, 1));

            EnsureContextData(); // loads the context constants before the first iteration
            ClearVariables();

            if (iteration == 0)
                RefreshNow();

            Iteration = iteration;
            Previous = previous == null || previous.Type.Base == Types.ErrorName ? Value.Null(PreviousConstant) : previous;
            UpdateHistory(iteration);
            SetPrevious(Previous);

            PreExecute();
        }

        /// <summary>
        /// <c>HISTORY</c> of the iteration: the results of the iterations before it; the last one is <c>PREV</c>.
        /// </summary>
        private void UpdateHistory(int iteration)
        {
            if (iteration == 0)
            {
                _history.Clear();
                _memory.Reset();
                CompletedIterations = 0;
                LastResult = null;
            }
            else
            {
                if (_history.Count > iteration - 1)
                    _history.RemoveRange(iteration - 1, _history.Count - (iteration - 1));

                while (_history.Count < iteration - 1)
                    _history.Add(Value.Null());

                _history.Add(Previous);
            }

            _historyValue = null;
        }

        /// <summary>
        /// Stores <c>PREV</c> in the container created by <see cref="LoadContextData"/>; a new constant only if the
        /// property was replaced from outside.
        /// </summary>
        private void SetPrevious(IValue previous)
        {
            if (_previous != null && HasKey(PreviousConstant) && ReferenceEquals(GetProperty(PreviousConstant), _previous))
                _previous.Replace(previous);
            else
                SetConstant(PreviousConstant, previous);
        }
    }

    /// <summary>
    /// State of the executed iterations of an Iteration Context (<see cref="IterationContext.SaveState"/>).
    /// </summary>
    /// <param name="CompletedIterations">Number of executed iterations; the next resumed iteration has this index.</param>
    /// <param name="LastResult">Result of the last executed iteration (<c>PREV</c> of the next one); <c>null</c> before the first one.</param>
    /// <param name="History"><c>HISTORY</c> of the last executed iteration: the results of the iterations before it.</param>
    /// <param name="Memory">The stored keys and values of the memory.</param>
    public sealed record IterationState(
        int CompletedIterations,
        IValue? LastResult,
        IReadOnlyList<IValue> History,
        IReadOnlyList<KeyValuePair<string, IValue>> Memory);
}
