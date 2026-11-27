using System.Runtime.CompilerServices;
using FormuLang.NET.Abstractions.Functions;
using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Processing;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Functions;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Structures;

namespace FormuLang.NET.Core.Processing
{
    /// <summary>
    /// Evaluation stage (docs/semantics/evaluation.md): resolves every sequence by traversing its parsing queue bottom-up,
    /// and stops the execution when the root of a sequence returns an Error other than <c>SUCCESS</c>.
    /// </summary>
    public class Evaluator : IEvaluator
    {
        public EvaluationResult Evaluate(ParsingResult formula, IContext context)
        {
            IValue last = Value.Error(Errors.Success(), "");

            foreach (Sequence sequence in formula.Sequences)
            {
                IValue result = EvaluateSequence(sequence, new Scope(context, null, null), isFormulaSequence: true);

                if (result.Type.Base == Types.ErrorName && result.As<IError>()!.IsFailed())
                {
                    if (!SuppressErrors(context))
                        return new EvaluationResult(result, result.As<IError>()!);

                    result = Value.Null();
                }

                last = result;
            }

            return new EvaluationResult(last, Errors.Success());
        }

        IEvaluationResult IEvaluator.Evaluate(IParsingResult formula, IContext context)
        {
            if (formula is not ParsingResult parsed)
                throw new ArgumentException($"{nameof(Evaluator)} evaluates only the result of {nameof(Parser)}.", nameof(formula));

            return Evaluate(parsed, context);
        }

        private static bool SuppressErrors(IContext context)
            => context.EnvironmentVariables.HasKey("suppress_errors")
               && context.EnvironmentVariables.GetProperty("suppress_errors").GetValue().Raw is true;

        /// <summary>
        /// Lambda parameters bound by one invocation, chained to the scope the lambda was created in.
        /// </summary>
        internal sealed class Scope(IContext context, IReadOnlyDictionary<string, IValue>? parameters, Scope? parent)
        {
            public IContext Context { get; } = context;

            public IValue? Lookup(string name)
                => parameters != null && parameters.TryGetValue(name, out IValue? value) ? value : parent?.Lookup(name);
        }

        /// <summary>
        /// Traverses the parsing queue: every node is resolved after all of its children.
        /// </summary>
        internal IValue EvaluateSequence(Sequence sequence, Scope scope, bool isFormulaSequence)
        {
            var results = new Dictionary<ExpressionNode, IValue>(ReferenceEqualityComparer.Instance);
            WriteTracker? tracker = scope.Context.Memory is ContextMemory memory ? new WriteTracker(memory) : null;
            IValue value;

            if (SkipsBranches(sequence, scope.Context.Environment))
            {
                value = EvaluateSelected(sequence.Root, sequence, results, scope, isFormulaSequence, tracker);
            }
            else
            {
                foreach (ExpressionNode node in sequence.Queue)
                {
                    bool declaration = isFormulaSequence && node == sequence.Root;
                    results[node] = EvaluateNode(node, results, scope, declaration, tracker);
                }

                value = results[sequence.Root];
            }

            if (tracker?.Writes != null)
                DiscardUnselectedWrites(sequence.Root, results, tracker);

            return value;
        }

        // Memory writes on the selected path only (docs/core/context.md, "Memory", rule 2)
        //
        // All the arguments of IF, AND and OR are computed before the call, so a STORE or CLEAR in a branch of IF that is
        // not selected, or in an argument of AND / OR after the deciding one, is executed too. Its write is collected with
        // the call node that made it (also through a lambda invoked by the call), and discarded when the sequence is
        // complete.

        /// <summary>
        /// Writes to the memory of the context made while a sequence is evaluated: the range of the collected writes made
        /// by every call node, so the writes of the nodes that are not on the selected path can be discarded.
        /// </summary>
        private sealed class WriteTracker(ContextMemory memory)
        {
            public ContextMemory Memory { get; } = memory;
            public Dictionary<ExpressionNode, (int From, int To)>? Writes { get; set; }
        }

        private const string AndName = "AND";
        private const string OrName = "OR";

        private static void DiscardUnselectedWrites(ExpressionNode root, Dictionary<ExpressionNode, IValue> results, WriteTracker tracker)
        {
            var selected = new HashSet<ExpressionNode>(ReferenceEqualityComparer.Instance);
            MarkSelected(root, results, selected);

            foreach (var (node, (from, to)) in tracker.Writes!)
            {
                if (!selected.Contains(node))
                    tracker.Memory.Discard(from, to);
            }
        }

        /// <summary>
        /// Marks the nodes on the selected path: the condition of <c>IF</c> and its selected branch (none for a condition
        /// that is not a Boolean, because <c>IF</c> then selects no branch), the arguments of <c>AND</c> / <c>OR</c> up to
        /// the deciding one, and every child of the other nodes.
        /// </summary>
        private static void MarkSelected(ExpressionNode node, Dictionary<ExpressionNode, IValue> results, HashSet<ExpressionNode> selected)
        {
            selected.Add(node);

            if (node is not CallNode call)
                return;

            IReadOnlyList<ExpressionNode> children = call.Children;

            if (call.Name == IfName && children.Count == 3)
            {
                MarkSelected(children[0], results, selected);

                if (results.TryGetValue(children[0], out IValue? condition) && condition.Raw is bool selectThen
                    && condition.Type.Base == Types.BooleanName)
                {
                    MarkSelected(selectThen ? children[1] : children[2], results, selected);
                }

                return;
            }

            if ((call.Name == AndName || call.Name == OrName) && children.Count >= 2)
            {
                bool deciding = call.Name == OrName;

                foreach (ExpressionNode child in children)
                {
                    MarkSelected(child, results, selected);

                    if (results.TryGetValue(child, out IValue? value) && value.Type.Base == Types.BooleanName && value.Raw is bool b && b == deciding)
                        return;
                }

                return;
            }

            foreach (ExpressionNode child in children)
                MarkSelected(child, results, selected);
        }

        // Skipping the branch of IF that is not selected (an optimization with the same result)
        //
        // IF receives both branches computed (docs/standards/core/functions/IF.md), so a branch that is not selected is
        // computed only for its side effects, and its value (even an Error) is discarded. In a sequence where every
        // function is side-effect free, the branch has no effects either, so it can be skipped, and the order of the
        // nodes cannot change a result. Only these sequences are evaluated top-down; the others follow the queue.

        private static readonly ConditionalWeakTable<Sequence, Tuple<IEnvironment, bool>> BranchAnalyses = new();

        /// <summary>
        /// Whether <paramref name="sequence"/> has an <c>IF</c> and only side-effect-free functions, apart from an
        /// assignment at its root (e.g. <c>$x = IF(...)</c>). The result is stored for every sequence and environment.
        /// </summary>
        private static bool SkipsBranches(Sequence sequence, IEnvironment environment)
        {
            if (BranchAnalyses.TryGetValue(sequence, out var known) && ReferenceEquals(known.Item1, environment))
                return known.Item2;

            bool hasIf = false;
            bool pure;

            if (sequence.Root is CallNode root && environment.GetFunction(root.Name) is Procedure { TakesVariable: true }
                && root.Children.Count > 0 && root.Children[0] is VariableNode)
            {
                pure = root.Children.Skip(1).All(child => IsSideEffectFree(child, environment, ref hasIf));
            }
            else
            {
                pure = IsSideEffectFree(sequence.Root, environment, ref hasIf);
            }

            bool result = pure && hasIf;
            BranchAnalyses.AddOrUpdate(sequence, Tuple.Create(environment, result));
            return result;
        }

        private static bool IsSideEffectFree(ExpressionNode node, IEnvironment environment, ref bool hasIf)
        {
            switch (node)
            {
                case LiteralNode or VariableNode or ConstantNode or ParameterNode:
                    return true;

                case LambdaNode lambda:
                    return IsSideEffectFree(lambda.Body.Root, environment, ref hasIf);

                case CallNode call:
                    // STORE and CLEAR are allowed: their writes in a branch that is not selected are discarded anyway
                    if (environment.GetFunction(call.Name) is not Function function
                        || !(function is Procedure { WritesMemoryOnly: true } || (function.SideEffectFree && function is not Procedure)))
                        return false;

                    if (call.Name == IfName)
                        hasIf = true;

                    for (int i = 0; i < call.Children.Count; i++)
                    {
                        ExpressionNode child = call.Children[i];

                        // A delegate argument is invoked by the function: it must be a lambda (checked as a node) or a
                        // side-effect-free named function, not a delegate from a variable whose body is not known here
                        if (TakesDelegate(function, i) && !IsKnownDelegate(child, environment))
                            return false;

                        if (!IsSideEffectFree(child, environment, ref hasIf))
                            return false;
                    }

                    return true;

                default:
                    return false;
            }
        }

        private const string IfName = "IF";

        private static bool TakesDelegate(Function function, int position)
            => function.Overloads.Any(o => o.Template.ElementAtOrDefault(position)?.Type.Base == Types.DelegateName
                || (o.Template.LastOrDefault() is { Multiple: true } last && position >= o.Template.Count() - 1
                    && last.Type.Base == Types.DelegateName));

        private static bool IsKnownDelegate(ExpressionNode node, IEnvironment environment) => node switch
        {
            LambdaNode => true,
            ConstantNode constant => environment.GetFunction(constant.Name) is null or Function { SideEffectFree: true },
            LiteralNode => true,
            _ => false,
        };

        /// <summary>
        /// Evaluates a node after its children, top-down, and only the selected branch of an <c>IF</c> whose condition is
        /// a Boolean. A condition of another type goes to <c>IF</c> with both branches, as in the queue.
        /// </summary>
        private IValue EvaluateSelected(ExpressionNode node, Sequence sequence, Dictionary<ExpressionNode, IValue> results,
            Scope scope, bool isFormulaSequence, WriteTracker? tracker)
        {
            if (results.TryGetValue(node, out IValue? known))
                return known;

            IValue value;

            if (node is CallNode { Name: IfName, Children.Count: 3 } call)
            {
                IValue condition = EvaluateSelected(call.Children[0], sequence, results, scope, isFormulaSequence, tracker);

                if (condition.Type.Base == Types.BooleanName)
                {
                    // IF(true, then, else) returns then, IF(false, then, else) returns else, unchanged
                    value = EvaluateSelected(condition.Raw is true ? call.Children[1] : call.Children[2], sequence, results, scope, isFormulaSequence, tracker);
                    results[node] = value;
                    return value;
                }
            }

            foreach (ExpressionNode child in node.Children)
                EvaluateSelected(child, sequence, results, scope, isFormulaSequence, tracker);

            value = EvaluateNode(node, results, scope, isFormulaSequence && node == sequence.Root, tracker);
            results[node] = value;
            return value;
        }

        private IValue EvaluateNode(ExpressionNode node, Dictionary<ExpressionNode, IValue> results, Scope scope, bool isRoot, WriteTracker? tracker)
        {
            switch (node)
            {
                case LiteralNode literal:
                    return literal.Value;

                case VariableNode variable:
                    return isRoot && variable.Variable.Scope == VariableScope.Context
                        ? ReadOrDeclare(variable.Variable.Name, scope.Context)
                        : ReadVariable(variable.Variable, scope.Context);

                case ConstantNode constant:
                    return ReadConstant(constant.Name, scope.Context);

                case ParameterNode parameter:
                    return scope.Lookup(parameter.Name)
                        ?? Value.Error(Errors.UnknownSymbolError($"Lambda parameter \"{parameter.Name}\" has no value."), parameter.Name);

                case LambdaNode lambda:
                    return Value.Delegate(new Lambda(lambda, this, scope), lambda.Key);

                case CallNode call:
                    return Call(call, results, scope.Context, tracker);

                default:
                    throw new NotSupportedException($"Unsupported expression node {node.GetType().Name}.");
            }
        }

        private static IValue Call(CallNode call, Dictionary<ExpressionNode, IValue> results, IContext context, WriteTracker? tracker)
        {
            IFunction? function = context.Environment.GetFunction(call.Name);

            if (function == null)
                return Value.Error(Errors.UnknownSymbolError($"Function \"{call.Name}\" is not defined."), call.Key);

            var arguments = new IValue[call.Children.Count];

            for (int i = 0; i < arguments.Length; i++)
            {
                ExpressionNode child = call.Children[i];

                // The variable argument of SET, INIT and COND_SET is the variable itself, not its value
                arguments[i] = i == 0 && function is Procedure { TakesVariable: true } && child is VariableNode variable
                    ? variable.Variable.ToValue()
                    : results[child];
            }

            if (tracker == null)
                return function.Invoke(arguments, context);

            int before = tracker.Memory.PendingCount;
            IValue value = function.Invoke(arguments, context);
            int after = tracker.Memory.PendingCount;

            if (after > before)
                (tracker.Writes ??= new(ReferenceEqualityComparer.Instance))[call] = (before, after);

            return value;
        }

        // Reading variables (docs/semantics/evaluation.md, "Direct extraction")

        /// <summary>
        /// A context variable at the root of a sequence is declared if it does not exist.
        /// </summary>
        private static IValue ReadOrDeclare(string name, IContext context)
        {
            if (context.HasKey(name))
                return context.GetProperty(name).GetValue();

            if (context.Environment.HasKey(name) || context.EnvironmentVariables.HasKey(name))
                return Value.Error(Errors.InvalidIdentifierError($"\"{name}\" symbol is already defined by environment."), "$" + name);

            context.InitVariable(name, Value.Undefined("$" + name));
            return Value.Error(Errors.Success(), "$" + name);
        }

        private static IValue ReadVariable(VariableReference variable, IContext context)
        {
            if (variable.Scope == VariableScope.Environment)
            {
                return context.EnvironmentVariables.HasKey(variable.Name)
                    ? context.EnvironmentVariables.GetProperty(variable.Name).GetValue()
                    : Value.Error(Errors.EnvironmentError($"Environment variable \"{variable.Name}\" is not defined."), variable.ToString());
            }

            return context.HasKey(variable.Name)
                ? context.GetProperty(variable.Name).GetValue()
                : Value.Error(Errors.UnknownSymbolError($"Variable \"{variable.Name}\" is not defined."), variable.ToString());
        }

        /// <summary>
        /// An identifier without a sigil: a context or environment constant, or a named function as a Delegate.
        /// </summary>
        private static IValue ReadConstant(string name, IContext context)
        {
            if (context.HasKey(name) && context.GetProperty(name).ContainerType == ContainerType.Constant)
                return context.GetProperty(name).GetValue();

            if (context.Environment.HasKey(name) && context.Environment.GetProperty(name).ContainerType == ContainerType.Constant)
                return context.Environment.GetProperty(name).GetValue();

            if (context.Environment.GetFunction(name) is IFunction function)
                return Value.Delegate(function, name);

            return Value.Error(Errors.UnknownSymbolError($"Constant \"{name}\" is not defined."), name);
        }
    }

    /// <summary>
    /// Anonymous function created by a lambda expression. Invoking it evaluates the stored body tree
    /// with the arguments bound to the parameters; the body is never parsed again.
    /// </summary>
    public sealed class Lambda : Primitive, ILambda
    {
        private readonly LambdaNode _node;
        private readonly Evaluator _evaluator;
        private readonly Evaluator.Scope _scope;

        public string Name => _node.Key;
        public FunctionType FunctionType => FunctionType.Lambda;
        public IEnumerable<IFunctionOverload> Overloads => [];
        public IReadOnlyList<string> Parameters => _node.Parameters;

        internal Lambda(LambdaNode node, Evaluator evaluator, Evaluator.Scope scope)
            : base(PrimitiveType.Function, Types.Delegate, node.Key)
        {
            _node = node;
            _evaluator = evaluator;
            _scope = scope;
        }

        public override bool CanAccessValue => true;
        public override IError CanUpdateValue => Errors.OperationError("A lambda function cannot be changed.");
        public override IValue GetValue() => Value.Delegate(this, Name);
        public override IError SetValue(IValue value) => CanUpdateValue;

        /// <summary>
        /// docs/semantics/evaluation.md, "Lambda invocation": no argument matching, only the count is checked.
        /// </summary>
        public IValue Invoke(IEnumerable<IValue> parameters, IContext context)
        {
            IValue[] args = parameters.ToArray();

            if (args.Length != Parameters.Count)
                return Value.Error(Errors.ArgumentError(
                    $"The lambda expects {Parameters.Count} argument(s), but {args.Length} were given."), Name);

            // docs/semantics/evaluation.md, "Invocation depth": a deeper invocation is an OPERATION_ERROR, never a stack overflow
            if (_depth >= MaxDepth)
                return Value.Error(Errors.OperationError(
                    $"Lambda invocations are nested more than {MaxDepth} deep (a recursion that does not stop?)."), Name);

            // A thread with a small stack (e.g. a host thread) could overflow before the limit: an Error is still better
            // than a StackOverflowException, which stops the whole process
            if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
                return Value.Error(Errors.OperationError("Lambda invocations are nested too deep for the stack of this thread."), Name);

            var bindings = new Dictionary<string, IValue>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
                bindings[Parameters[i]] = args[i];

            _depth++;
            try
            {
                return _evaluator.EvaluateSequence(_node.Body, new Evaluator.Scope(_scope.Context, bindings, _scope), isFormulaSequence: false);
            }
            finally
            {
                _depth--;
            }
        }

        /// <summary>
        /// Maximum number of lambda invocations running one inside another (docs/semantics/evaluation.md).
        /// </summary>
        public const int MaxDepth = 256;

        // Per thread: the executions of different threads do not share the depth
        [ThreadStatic] private static int _depth;

        public override string ToString() => $"({string.Join(", ", Parameters)}) => any";
    }

    public sealed class EvaluationResult(IValue value, IError error) : IEvaluationResult
    {
        public IValue Value { get; } = value;
        public IError Error { get; } = error;
    }
}
