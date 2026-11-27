using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Structures;

namespace FormuLang.NET.Core.Structures
{
    /// <summary>
    /// Data of formula executions (docs/core/context.md). Stores the context ($) variables, the context constants and
    /// the copies of the environment (@) variables.
    /// </summary>
    /// <remarks>
    /// The host application chooses the context type (docs/core/context.md, "Context types"). This class is the Core
    /// <b>Default Context</b>, which has no constants of its own. A context type that provides its own data derives from
    /// it and loads that data in <see cref="LoadContextData"/>, e.g. <see cref="IterationContext"/>.
    /// </remarks>
    public class Context : Primitives.Object, IContext
    {
        private readonly Primitives.Object _environmentVariables = new();
        private bool _contextDataLoaded;

        public IEnvironment Environment { get; }

        public IObject EnvironmentVariables => _environmentVariables;

        /// <summary>
        /// Context kind: disposable or persistent (docs/core/context.md, "Context kinds").
        /// </summary>
        public ContextType ContextType { get; }

        /// <summary>
        /// Memory of the context (docs/core/context.md, "Memory", since <c>core-1.2</c>). The Default Context has none:
        /// <c>STORE</c>, <c>CLEAR</c> and <c>RECALL</c> are an <c>OPERATION_ERROR</c> there.
        /// </summary>
        public virtual ContextMemory? Memory => null;

        IMemory? IContext.Memory => Memory;

        private static readonly IReadOnlyList<string> DefaultTypeNames = ["Default"];

        /// <summary>
        /// Names of the context type and of its base types, from the most derived one (docs/standards/core/README.md,
        /// "Metadata", key <c>context</c>), e.g. <c>["Iteration", "Default"]</c>. The <c>METADATA_CONTEXT</c> warning
        /// compares the <c>context</c> key of the metadata with them.
        /// </summary>
        public virtual IReadOnlyList<string> ContextTypeNames => DefaultTypeNames;

        /// <summary>
        /// Finalization (docs/semantics/finalization.md) of the result of an execution in this context: a context type
        /// with rules for its expected result (e.g. the signal object of a Stock strategy) returns an Error for a value
        /// that breaks them. Called by the executor before the writes to the memory are applied. Returns the value
        /// unchanged by default.
        /// </summary>
        public virtual IValue FinalizeResult(IValue value) => value;

        protected Context(IEnvironment environment, ContextType contextType)
        {
            Environment = environment;
            ContextType = contextType;
        }

        /// <summary>
        /// Creates a context and runs the pre-execution.
        /// </summary>
        /// <param name="contextType">Context kind: disposable for a single execution; persistent to keep the variables between executions.</param>
        public static Context Create(IEnvironment environment, ContextType contextType = ContextType.Disposable)
        {
            var context = new Context(environment, contextType);
            context.PreExecute();
            return context;
        }

        /// <summary>
        /// Pre-execution (docs/semantics/pre-execution.md): loads the data of the context type (once), then copies the
        /// environment variables that the context does not contain yet, so the state of a reused persistent context is
        /// never overwritten. <see cref="Processing.Executor"/> calls it before every execution.
        /// </summary>
        public void PreExecute()
        {
            EnsureContextData();

            // Step 2: NOW is set once for the whole execution
            if (RefreshesNowOnPreExecute || !HasKey(Structures.Environment.NowConstant))
                RefreshNow();

            foreach (string key in Environment.Keys)
            {
                IContainer container = Environment.GetProperty(key);

                if (container.ContainerType == ContainerType.Variable && !_environmentVariables.HasKey(key))
                    _environmentVariables.InitVariable(key, container.GetValue());
            }
        }

        /// <summary>
        /// Pre-execution step 2: loads the constants and parameters required by the context type, e.g. with
        /// <see cref="Primitives.Object.SetConstant"/>. Called once, before the first execution in this context.
        /// The basic Core context has no data of its own.
        /// </summary>
        protected virtual void LoadContextData() { }

        /// <summary>
        /// Loads the data of the context type (<see cref="LoadContextData"/>) if it is not loaded yet, without the rest
        /// of the pre-execution. Used before the first iteration or frame, which runs the full pre-execution later.
        /// </summary>
        protected void EnsureContextData()
        {
            if (_contextDataLoaded)
                return;

            _contextDataLoaded = true;
            LoadContextData();
        }

        /// <summary>
        /// Whether every <see cref="PreExecute"/> sets <c>NOW</c> to the current time. The Default Context does; the
        /// Iteration Context sets it once before the first iteration, the Time Context once for every frame.
        /// </summary>
        protected virtual bool RefreshesNowOnPreExecute => true;

        /// <summary>
        /// <c>NOW</c> of every execution in this context instead of the clock of the environment, or <c>null</c>. A
        /// context created by a function for its caller (e.g. a child Indicator Context) uses the <c>NOW</c> of the caller.
        /// </summary>
        public IValue? FixedNow { get; set; }

        /// <summary>
        /// <c>NOW</c> of the current execution, or <c>null</c> before the first pre-execution.
        /// </summary>
        public IValue? Now => HasKey(Structures.Environment.NowConstant)
            ? GetProperty(Structures.Environment.NowConstant).GetValue()
            : null;

        /// <summary>
        /// Sets the <c>NOW</c> constant of the execution (docs/semantics/pre-execution.md, step 2): to
        /// <paramref name="now"/>, or to the time read with <see cref="Structures.Environment.ReadNow"/>. The value is
        /// stored in the context, so it does not change while the formula is executed.
        /// </summary>
        protected void RefreshNow(IValue? now = null)
        {
            now ??= FixedNow ?? Structures.Environment.ReadNow(Environment);

            if (now != null)
                SetConstant(Structures.Environment.NowConstant, now);
        }

        /// <summary>
        /// Removes every context variable and every copy of an environment variable; context constants are kept.
        /// The next <see cref="PreExecute"/> copies the environment variables again.
        /// </summary>
        /// <remarks>
        /// Runs before every iteration or frame, so it avoids work in the common case: a copy of an environment variable
        /// that the formula did not change still holds the value of the environment and is kept, and a changed copy gets
        /// the value of the environment back. The result is the same as removing every copy and copying them again.
        /// </remarks>
        protected void ClearVariables()
        {
            List<string>? variables = null;

            foreach (KeyValuePair<string, Primitives.Container> property in _properties)
            {
                if (property.Value.ContainerType == ContainerType.Variable)
                    (variables ??= []).Add(property.Key);
            }

            if (variables != null)
            {
                foreach (string key in variables)
                    RemoveProperty(key);
            }

            List<string>? removed = null;

            foreach (string key in _environmentVariables.Keys)
            {
                IContainer copy = _environmentVariables.GetProperty(key);
                IContainer? original = Environment.HasKey(key) ? Environment.GetProperty(key) : null;

                if (original == null || original.ContainerType != ContainerType.Variable)
                    (removed ??= []).Add(key);
                else if (!ReferenceEquals(copy.GetValue(), original.GetValue()))
                    _environmentVariables.SetContainer(new Primitives.Variable(key, original.GetValue()));
            }

            if (removed != null)
            {
                foreach (string key in removed)
                    _environmentVariables.RemoveProperty(key);
            }
        }

        /// <summary>
        /// Reads a copied environment variable, e.g. <c>array_indexing</c>.
        /// </summary>
        public IValue? GetEnvironmentVariable(string name)
            => _environmentVariables.HasKey(name) ? _environmentVariables.GetProperty(name).GetValue() : null;
    }
}
