using System.Runtime.CompilerServices;
using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Abstractions.Processing;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Structures;
using FormuLang.NET.Core.Utils;

namespace FormuLang.NET.Core.Processing
{
    /// <summary>
    /// Runs the whole execution (docs/semantics/execution.md): pre-execution, validation, metadata reading, clean-up,
    /// tokenization, parsing, evaluation and finalization. The pipeline stops at the first stage that fails.
    /// </summary>
    /// <remarks>
    /// The steps up to the parsing are the <b>preparation</b> (<see cref="Prepare"/>), which gives a prepared formula; a
    /// <b>hot execution</b> evaluates a prepared formula again without preparing it (<see cref="Evaluate"/>, and every
    /// iteration of <see cref="ExecuteIterations(string, IterationContext, bool)"/>). It gives the same result as a cold
    /// execution. Every stage can be replaced with its own implementation.
    /// </remarks>
    public class Executor
    {
        private IValidator? _validator = new Validator();
        private ICleaner _cleaner = new Cleaner();
        private ITokenizer _tokenizer = new Tokenizer();
        private IParser? _parser;
        private IEvaluator _evaluator = new Evaluator();

        // Parser of an environment, kept while the environment has the same functions
        private readonly ConditionalWeakTable<IEnvironment, Tuple<int, IParser>> _parsers = new();

        // Contexts whose environment was checked before their first hot execution
        private readonly ConditionalWeakTable<IContext, object> _checkedContexts = new();

        /// <summary>
        /// Executor with the Core implementation of every stage.
        /// </summary>
        public static Executor Create() => new();

        /// <param name="validator">Validator, or <c>null</c> to skip validation.</param>
        public Executor WithValidator(IValidator? validator)
        {
            _validator = validator;
            return this;
        }

        public Executor WithCleaner(ICleaner cleaner)
        {
            _cleaner = cleaner;
            return this;
        }

        public Executor WithTokenizer(ITokenizer tokenizer)
        {
            _tokenizer = tokenizer;
            return this;
        }

        /// <param name="parser">Parser, or <c>null</c> to use the Core parser with the procedures of the context's environment.</param>
        public Executor WithParser(IParser? parser)
        {
            _parser = parser;
            return this;
        }

        public Executor WithEvaluator(IEvaluator evaluator)
        {
            _evaluator = evaluator;
            return this;
        }

        /// <summary>
        /// Shortcut: executes the formula in a new basic, disposable Core context (<see cref="Context.Create"/>).
        /// The host application chooses the context type; for any other type (a persistent context, or a context that
        /// provides its own constants) create the context and use <see cref="Execute(string, IContext)"/>.
        /// </summary>
        public ExecutionResult Execute(string formula, IEnvironment environment)
            => Execute(formula, Context.Create(environment));

        /// <summary>
        /// Executes the formula in the context chosen by the host application (a cold execution). Reuse the context to
        /// keep its variables between executions. The result has the warnings of the preparation.
        /// </summary>
        public ExecutionResult Execute(string formula, IContext context)
        {
            // Pre-execution
            if (context is Context core)
                core.PreExecute();

            PreparedFormula prepared = Prepare(formula, context);
            return prepared.Failure ?? EvaluatePrepared(prepared.Parsing, context, prepared.Warnings);
        }

        /// <summary>
        /// Evaluates a prepared formula in a context (a hot execution): the evaluation and the finalization, after the
        /// pre-execution of the context. The checks of the environment are done once for every context; a formula that
        /// was prepared for another environment, or before the functions of the environment changed, is prepared again.
        /// </summary>
        public ExecutionResult Evaluate(IPreparedFormula formula, IContext context)
        {
            if (context is Context core)
                core.PreExecute();

            return PreparedFor(formula, context, out IParsingResult? parsing) ?? EvaluatePrepared(parsing!, context, []);
        }

        /// <summary>
        /// Executes the formula once for every iteration of an Iteration Context (docs/core/context.md, "Iteration
        /// Context"), from the first to the last. The formula is prepared (validated, cleaned up, tokenized and parsed)
        /// only once. The first iteration starts with an empty <c>HISTORY</c> and an empty memory.
        /// </summary>
        /// <param name="stopOnError">
        /// The host application decides whether the remaining iterations are executed after an iteration results in an
        /// Error: <c>true</c> stops at the first Error, <c>false</c> executes every iteration (<c>PREV</c> is then
        /// <c>null</c> after a failed iteration).
        /// </param>
        public IterationExecutionResult ExecuteIterations(string formula, IterationContext context, bool stopOnError = true)
            => ExecuteIterations(new FormulaSource(formula), context, stopOnError);

        /// <inheritdoc cref="ExecuteIterations(string, IterationContext, bool)"/>
        public IterationExecutionResult ExecuteIterations(IPreparedFormula formula, IterationContext context, bool stopOnError = true)
            => ExecuteIterations(new FormulaSource(formula), context, stopOnError);

        private IterationExecutionResult ExecuteIterations(FormulaSource formula, IterationContext context, bool stopOnError)
        {
            if (context.Count == 0)
                return new IterationExecutionResult([]);

            context.BeginIteration(0, null);

            if (PreparedFor(formula, context, out IParsingResult? parsing) is ExecutionResult failure)
                return new IterationExecutionResult([failure]);

            return Iterate(parsing!, context, 0, stopOnError);
        }

        /// <summary>
        /// Continues the iterations of a context after the last iteration executed by
        /// <see cref="ExecuteIterations(string, IterationContext, bool)"/> or by this method (docs/core/context.md,
        /// "Resumed iterations"), e.g. after the host application added elements to the collection
        /// (<see cref="IterationContext.Extend"/>). <c>PREV</c>, <c>HISTORY</c> and the memory of the executed iterations
        /// are kept, so the results are the same as when all the iterations are executed again. Without executed
        /// iterations it is <see cref="ExecuteIterations(string, IterationContext, bool)"/>.
        /// </summary>
        /// <returns>The results of the resumed iterations only.</returns>
        public IterationExecutionResult ResumeIterations(string formula, IterationContext context, bool stopOnError = true)
            => ResumeIterations(new FormulaSource(formula), context, stopOnError);

        /// <inheritdoc cref="ResumeIterations(string, IterationContext, bool)"/>
        public IterationExecutionResult ResumeIterations(IPreparedFormula formula, IterationContext context, bool stopOnError = true)
            => ResumeIterations(new FormulaSource(formula), context, stopOnError);

        private IterationExecutionResult ResumeIterations(FormulaSource formula, IterationContext context, bool stopOnError)
        {
            int start = context.CompletedIterations;

            if (start == 0)
                return ExecuteIterations(formula, context, stopOnError);

            if (start >= context.Count)
                return new IterationExecutionResult([]);

            if (PreparedFor(formula, context, out IParsingResult? parsing) is ExecutionResult failure)
                return new IterationExecutionResult([failure]);

            return Iterate(parsing!, context, start, stopOnError);
        }

        private IterationExecutionResult Iterate(IParsingResult parsing, IterationContext context, int start, bool stopOnError)
        {
            var results = new List<ExecutionResult>(context.Count - start);
            IValue? previous = start == 0 ? null : context.LastResult;

            for (int i = start; i < context.Count; i++)
            {
                if (i > 0)
                    context.BeginIteration(i, previous);

                ExecutionResult result = EvaluatePrepared(parsing, context, []);
                context.CompleteIteration(result.Value);
                results.Add(result);

                if (!result.Successful && stopOnError)
                    break;

                previous = result.Value;
            }

            return new IterationExecutionResult(results);
        }

        /// <summary>
        /// Prepares the execution of a formula in the frames of a Time Context (docs/core/context.md, "Time Context").
        /// The formula is validated, tokenized and parsed only once; every <see cref="FrameRunner.NextFrame"/> executes
        /// one frame.
        /// </summary>
        public FrameRunner CreateFrameRunner(string formula, TimeContext context)
            => CreateFrameRunner(new FormulaSource(formula), context);

        /// <inheritdoc cref="CreateFrameRunner(string, TimeContext)"/>
        public FrameRunner CreateFrameRunner(IPreparedFormula formula, TimeContext context)
            => CreateFrameRunner(new FormulaSource(formula), context);

        private FrameRunner CreateFrameRunner(FormulaSource formula, TimeContext context)
        {
            context.BeginFrame(0, 0, 0, null);
            ExecutionResult? failure = PreparedFor(formula, context, out IParsingResult? parsing);
            return new FrameRunner(this, context, parsing, failure);
        }

        /// <summary>
        /// Executes the formula once for every frame (time and delta in seconds, optional <c>NOW</c> in milliseconds).
        /// The formula is parsed only once.
        /// </summary>
        /// <param name="stopOnError">
        /// <c>true</c> stops at the first frame that results in an Error, <c>false</c> executes every frame.
        /// </param>
        public IterationExecutionResult ExecuteFrames(string formula, TimeContext context,
            IEnumerable<(double TimeSeconds, double DeltaSeconds, double? Now)> frames, bool stopOnError = true)
        {
            FrameRunner runner = CreateFrameRunner(formula, context);
            var results = new List<ExecutionResult>();

            foreach (var (time, delta, now) in frames)
            {
                ExecutionResult result = runner.NextFrame(time, delta, now);
                results.Add(result);

                if (runner.PreparationError != null || (!result.Successful && stopOnError))
                    break;
            }

            return new IterationExecutionResult(results);
        }

        internal ExecutionResult EvaluateFrame(IParsingResult parsing, IContext context) => EvaluatePrepared(parsing, context, []);

        /// <summary>
        /// The preparation (docs/semantics/execution.md, "Execution modes"): validation, metadata reading, clean-up,
        /// tokenization and parsing. Returns the prepared formula, with its metadata and warnings, or a failed one
        /// (<see cref="PreparedFormula.Failure"/>) with the result of the stage that stopped. A prepared formula can be
        /// evaluated many times with <see cref="Evaluate"/>.
        /// </summary>
        public PreparedFormula Prepare(string formula, IContext context)
        {
            IEnvironment environment = context.Environment;

            // 1. Validation
            if (_validator != null)
            {
                IValidationResult validation = _validator.Validate(formula, environment);
                if (validation.Errors.Count > 0)
                    return PreparedFormula.Failed(formula, new ExecutionResult(Value.Error(validation.Errors[0], formula), validation.Errors, context));
            }

            _checkedContexts.AddOrUpdate(context, true);

            // 2. Metadata reading
            MetadataResult metadata = MetadataReader.Read(formula);
            if (metadata.Error.IsFailed())
                return Fail(metadata.Error, formula, context);

            // 3. Clean-up
            ICleanUpResult cleanUp;
            CleanUpPositions? positions = null;

            if (_cleaner is Cleaner)
                (cleanUp, positions) = Cleaner.CleanUpWithPositions(metadata.Formula);
            else
                cleanUp = _cleaner.CleanUp(metadata.Formula);

            if (cleanUp.Error.IsFailed())
                return Fail(cleanUp.Error, formula, context);

            // 4. Tokenization
            ITokenizationResult tokenization = _tokenizer.Tokenize(cleanUp.Result);
            if (tokenization.Error.IsFailed())
                return Fail(tokenization.Error, formula, context);

            IEnumerable<IToken> tokens = positions == null ? tokenization.Tokens : OriginalPositions(tokenization.Tokens, positions);

            // 5. Parsing
            IParsingResult parsing = ParserFor(environment).Parse(tokens);
            if (parsing.Error.IsFailed())
                return Fail(parsing.Error, formula, context);

            // Warnings
            List<IWarning> warnings = Warnings.SequenceWarnings(parsing, environment);
            if (metadata.Position is SourcePosition position)
                warnings.AddRange(Warnings.MetadataWarnings(metadata.Metadata, position, context));

            return new PreparedFormula(formula, parsing, metadata.Metadata, Warnings.Sort(warnings), environment, FunctionCount(environment));
        }

        /// <summary>
        /// The validation and the metadata reading only (docs/core/metadata.md, "Metadata reading"), e.g. to show the
        /// parameters of a formula in an editor. Without an environment only the metadata reading is done.
        /// </summary>
        public MetadataResult ReadMetadata(string formula, IEnvironment? environment = null)
        {
            if (_validator != null && environment != null)
            {
                IValidationResult validation = _validator.Validate(formula, environment);
                if (validation.Errors.Count > 0)
                    return new MetadataResult(Value.Object(new Primitives.Object(), "{}"), formula, null, validation.Errors[0]);
            }

            return MetadataReader.Read(formula);
        }

        /// <summary>
        /// A formula text, or a formula prepared by <see cref="Prepare"/>.
        /// </summary>
        private readonly record struct FormulaSource(string? Text, IPreparedFormula? Prepared)
        {
            public FormulaSource(string text) : this(text, null) { }
            public FormulaSource(IPreparedFormula prepared) : this(null, prepared) { }
        }

        private ExecutionResult? PreparedFor(FormulaSource formula, IContext context, out IParsingResult? parsing)
            => formula.Text != null ? PreparedFor(Prepare(formula.Text, context), context, out parsing) : PreparedFor(formula.Prepared!, context, out parsing);

        /// <summary>
        /// The parsing result for the context: of the given prepared formula (after the checks of the environment, once
        /// for every context), or of a new preparation if it was prepared for another environment.
        /// </summary>
        /// <returns>The failed result, or <c>null</c> if <paramref name="parsing"/> is ready to be evaluated.</returns>
        private ExecutionResult? PreparedFor(IPreparedFormula formula, IContext context, out IParsingResult? parsing)
        {
            parsing = null;

            if (formula is PreparedFormula prepared)
            {
                if (prepared.Failure != null)
                    return prepared.Failure;

                if (!prepared.IsPreparedFor(context.Environment))
                    return PreparedFor(Prepare(formula.Formula, context), context, out parsing);

                parsing = prepared.Parsing;
            }
            else
            {
                if (formula.Error.IsFailed())
                    return Fail(formula.Error, formula.Formula, context).Failure;

                parsing = formula;
            }

            if (!_checkedContexts.TryGetValue(context, out _))
            {
                if (_validator is Validator)
                {
                    ValidationResult validation = Validator.ValidateEnvironment(context.Environment);
                    if (validation.Errors.Count > 0)
                    {
                        parsing = null;
                        return new ExecutionResult(Value.Error(validation.Errors[0], formula.Formula), validation.Errors, context);
                    }
                }
                else if (_validator != null)
                {
                    return PreparedFor(Prepare(formula.Formula, context), context, out parsing);
                }

                _checkedContexts.AddOrUpdate(context, true);
            }

            return null;
        }

        private IParser ParserFor(IEnvironment environment)
        {
            if (_parser != null)
                return _parser;

            int count = FunctionCount(environment);

            if (_parsers.TryGetValue(environment, out var cached) && cached.Item1 == count)
                return cached.Item2;

            Parser parser = Parser.ForEnvironment(environment);
            _parsers.AddOrUpdate(environment, Tuple.Create(count, (IParser)parser));
            return parser;
        }

        internal static int FunctionCount(IEnvironment environment)
            => environment is Structures.Environment core ? core.FunctionCount : environment.Functions.Count();

        /// <summary>
        /// Stages 6-7: evaluation and finalization; the writes to the memory are applied if the result is not an Error.
        /// </summary>
        private ExecutionResult EvaluatePrepared(IParsingResult parsing, IContext context, IReadOnlyList<IWarning> warnings)
        {
            IValue value = _evaluator.Evaluate(parsing, context).Value;

            if (context is Context core)
                value = core.FinalizeResult(value);

            if (context.Memory is ContextMemory memory)
            {
                if (value.Type.Base == Types.ErrorName && value.Raw is IError error && error.IsFailed())
                    memory.Rollback();
                else
                    memory.Commit();
            }

            return new ExecutionResult(value, [], context, warnings);
        }

        private static PreparedFormula Fail(IError error, string formula, IContext context)
            => PreparedFormula.Failed(formula, new ExecutionResult(Value.Error(error, formula), [], context));

        /// <summary>
        /// The tokens with the positions of their first characters in the original formula.
        /// </summary>
        private static IEnumerable<IToken> OriginalPositions(IEnumerable<IToken> tokens, CleanUpPositions positions)
            => tokens.Select(token =>
            {
                if (token.Line is not int line || token.Column is not int column || positions.At(line, column) is not SourcePosition position)
                    return token;

                return new Token(token.Type, token.Value, position.Line, position.Column);
            }).ToArray();
    }

    /// <summary>
    /// A prepared formula (docs/semantics/execution.md, "Execution modes", since <c>core-1.2</c>): the parsed sequences,
    /// the metadata and the warnings of the preparation. A hot execution evaluates it without preparing the formula again
    /// (<see cref="Executor.Evaluate"/>, <see cref="Executor.ExecuteIterations(IPreparedFormula, IterationContext, bool)"/>),
    /// as long as the functions of the environment do not change.
    /// </summary>
    public sealed class PreparedFormula : IPreparedFormula
    {
        private readonly IEnvironment? _environment;
        private readonly int _functionCount;

        public string Formula { get; }

        /// <summary>
        /// The result of the parsing; a failed parsing result if the preparation failed.
        /// </summary>
        public IParsingResult Parsing { get; }

        public IValue Metadata { get; }

        public IReadOnlyList<IWarning> Warnings { get; }

        /// <summary>
        /// The failed result of the stage that stopped the preparation, or <c>null</c> if the formula is ready to be
        /// evaluated.
        /// </summary>
        public ExecutionResult? Failure { get; }

        public IReadOnlyList<ISequence> Sequences => Parsing.Sequences;

        public IReadOnlyDictionary<string, IExpressionNode> PrimitiveMap => Parsing.PrimitiveMap;

        /// <summary>
        /// <c>SUCCESS</c>, or the Error that stopped the preparation.
        /// </summary>
        public IError Error => Failure?.Error ?? Parsing.Error;

        public bool Successful => Error.IsSuccess();

        internal PreparedFormula(string formula, IParsingResult parsing, IValue metadata, IReadOnlyList<IWarning> warnings,
            IEnvironment environment, int functionCount)
        {
            Formula = formula;
            Parsing = parsing;
            Metadata = metadata;
            Warnings = warnings;
            _environment = environment;
            _functionCount = functionCount;
        }

        private PreparedFormula(string formula, ExecutionResult failure)
        {
            Formula = formula;
            Parsing = ParsingResult.Failed(failure.Error);
            Metadata = Value.Object(new Primitives.Object(), "{}");
            Warnings = [];
            Failure = failure;
        }

        internal static PreparedFormula Failed(string formula, ExecutionResult failure) => new(formula, failure);

        /// <summary>
        /// Whether the formula can be evaluated in an environment without being prepared again.
        /// </summary>
        public bool IsPreparedFor(IEnvironment environment)
            => Failure == null && ReferenceEquals(environment, _environment) && Executor.FunctionCount(environment) == _functionCount;
    }

    public sealed class ExecutionResult : IExecutionResult
    {
        public IValue Value { get; }
        public IReadOnlyList<IError> ValidationErrors { get; }
        public IContext Context { get; }

        /// <summary>
        /// Warnings of the preparation of the formula (since <c>core-1.2</c>); they never change the result. Only a cold
        /// execution (<see cref="Executor.Execute(string, IContext)"/>) has them; see <see cref="PreparedFormula.Warnings"/>.
        /// </summary>
        public IReadOnlyList<IWarning> Warnings { get; }

        /// <summary>
        /// The Error that aborted the execution, or <c>SUCCESS</c>.
        /// </summary>
        public IError Error { get; }

        public bool Successful => Error.IsSuccess();

        public ExecutionResult(IValue value, IReadOnlyList<IError> validationErrors, IContext context)
            : this(value, validationErrors, context, []) { }

        public ExecutionResult(IValue value, IReadOnlyList<IError> validationErrors, IContext context, IReadOnlyList<IWarning> warnings)
        {
            Value = value;
            ValidationErrors = validationErrors;
            Context = context;
            Warnings = warnings;
            Error = value.Type.Base == Types.ErrorName && value.Raw is IError error && error.IsFailed()
                ? error
                : Errors.Success();
        }

        /// <summary>
        /// The serialized value, with the serialization parameters of the execution (e.g. <c>@decimal_place = 2</c>).
        /// </summary>
        public override string ToString() => StringConverter.ValueToString(Value, SerializationOptions.FromContext(Context));
    }

    /// <summary>
    /// Results of the iterations of an Iteration Context, in order (<see cref="Executor.ExecuteIterations(string, IterationContext, bool)"/>).
    /// </summary>
    public sealed class IterationExecutionResult
    {
        /// <summary>
        /// One result for every executed iteration. If the formula cannot be parsed, there is only the failed result.
        /// </summary>
        public IReadOnlyList<ExecutionResult> Results { get; }

        /// <summary>
        /// Values of the iterations, in order.
        /// </summary>
        public IEnumerable<IValue> Values => Results.Select(r => r.Value);

        /// <summary>
        /// The first Error that aborted an iteration, or <c>SUCCESS</c>.
        /// </summary>
        public IError Error => Results.FirstOrDefault(r => !r.Successful)?.Error ?? Errors.Success();

        public bool Successful => Error.IsSuccess();

        public IterationExecutionResult(IReadOnlyList<ExecutionResult> results)
        {
            Results = results;
        }
    }
}
