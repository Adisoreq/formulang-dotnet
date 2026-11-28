# Architecture

## Modules

```
FormuLang.NET.Abstractions  ◄──  FormuLang.NET.Core  ◄──  FormuLang.NET.Extensions.*
        (interfaces)              (Core standard)           (extension standards)
```

| Project | Depends on | Responsibility |
| :------ | :--------- | :------------- |
| `FormuLang.NET.Abstractions` | - | Contracts shared by every module. A host application or an extension can depend only on this project. |
| `FormuLang.NET.Core` | Abstractions | Implementation of the [Core standard](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/README.md) and of the execution pipeline. |
| `FormuLang.NET.Extensions.Stock` | Abstractions, Core | Implementation of the Stock extension standard: `Stock.Candle`, the Stock context types, the formula registry, the Stock functions (`LOAD_INDICATOR`, `LOAD_PATTERN`, `LOAD_CANDLES`, the series functions, `SCORE`, `SIGNAL_SCORE`, ...) and the formula models (`Models`). |
| `FormuLang.NET.Core.Tests`, `FormuLang.NET.Extensions.Stock.Tests` | Core, Extensions.Stock | Tests built from the examples of the standard documents (xUnit). |
| `FormuLang.NET.Conformance.Tests` | Core, Extensions.Stock | Runner of the language-independent conformance tests of the standard (JSON test cases copied from the standard repository). |
| `CalculatorCLI` (sample) | Core, Extensions.Stock | Executes formulas from the command line. |
| `WebPreview` (sample) | Core, Extensions.Stock | Blazor WebAssembly editor that executes formulas of every context type in the browser. |

Every extension standard gets its own `FormuLang.NET.Extensions.<Name>` project, so a host application references only the standards it supports.

## Namespaces

| Namespace | Abstractions | Core |
| :-------- | :----------- | :--- |
| `.Primitives` | `IPrimitive`, `IValue`, `IType`, `IContainer`, `IObject`, `IError`, `PrimitiveType`, `ContainerType`, `FunctionType` | `Primitive`, `Value`, `Types` (type factory), `Container`, `Variable`, `Constant`, `Object`, `Error`, `VariableReference` |
| `.Functions` | `IFunction`, `ILambda`, `IFunctionOverload`, `IArgumentTemplate`, `IInvokable` | `Function`, `Procedure`, `FunctionOverload`, `ArgumentTemplate` |
| `.Operators` | `IOperator`, `OperatorType`, `Associativity` | `Operators` (operator table, in the root namespace) |
| `.Processing` | Stage interfaces (`IValidator`, `ICleaner`, `ITokenizer`, `IParser`, `IEvaluator`), their results, `IToken`, `IExpressionNode`, `ISequence`, `IExecutionResult`, `IMetadataResult`, `IPreparedFormula`, `IWarning`, `WarningCode` | `Executor`, `PreparedFormula`, `Validator`, `MetadataReader`, `Cleaner`, `Tokenizer`, `Parser`, `Evaluator`, `Lambda`, expression nodes, `Warning`, `Warnings`, `MetadataChecks` |
| `.Structures` | `IEnvironment`, `IContext`, `IMemory`, `ContextType`, `IStandard`, `IStandardVersion`, `IAbstractTree<T>` | `Environment`, `Context`, `IterationContext`, `IterationState`, `TimeContext`, `ContextMemory`, `Standard`, `StandardVersion`, `AbstractTree<T>` |
| `.Model` | - | Formula models of the Core: `ParameterSpec`, `Parameters` (normalization and key), `ParameterProblem`, `ValueConversion` (.NET and JSON values) |
| `.Standards` | - | `CoreStandard`, `CoreFunctions`, `CoreProcedures` |
| `.Validation` | - | `IdentifierValidator`, `NumberValidator` |
| (root) | - | `Errors` (Core error factory), `Operators` |

The standard calls the whole process **execution**; **evaluation** is only its sixth stage. The stages live in the `Processing` namespace, and `Executor` runs them in order.

## Execution pipeline

| # | Stage | Interface | Core implementation | Output |
| :-: | :---- | :-------- | :------------------ | :----- |
| | [Pre-execution](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/pre-execution.md) | - | `Context.Create`, `Context.PreExecute` | context with copies of the environment variables |
| 1 | [Validation](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/validation.md) | `IValidator` | `Validator` | every error found, at once |
| 2 | [Metadata reading](https://github.com/Adisoreq/FormuLang/blob/main/docs/core/metadata.md#metadata-reading) | - | `MetadataReader` | metadata Object, formula with the block replaced by white space |
| 3 | [Clean-up](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/cleanup.md) | `ICleaner` | `Cleaner` | cleaned text |
| 4 | [Tokenization](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/tokenization.md) | `ITokenizer` | `Tokenizer` | tokens |
| 5 | [Parsing](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/parsing.md) | `IParser` | `Parser` | one tree and parsing queue per sequence, primitive map |
| 6 | [Evaluation](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/evaluation.md) | `IEvaluator` | `Evaluator` | value of the last sequence, or the aborting Error |
| 7 | [Finalization](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/finalization.md) | - | `Executor`, `Context.FinalizeResult` | `ExecutionResult` |

The pipeline stops at the first stage that fails. Validation is the only stage that reports every problem it finds.

Stages 1 - 5 are the **preparation** (`Executor.Prepare`): it gives a `PreparedFormula` with the parsed sequences, the metadata and the warnings (`Warnings`). A **hot execution** (`Executor.Evaluate`, every iteration of `ExecuteIterations`) evaluates a prepared formula again. The clean-up keeps the original position of every character (`Cleaner.CleanUpWithPositions`), so the tokens, the warnings and the parsing error of a line that starts with a binary operator have the lines and columns of the original formula.

### Memory

`ContextMemory` collects the writes of `STORE` and `CLEAR`. The evaluator records the range of the writes made by every call node of a sequence (also through the lambdas it invokes); when the sequence is complete, the writes of the nodes that are not on the selected path (a branch of `IF` that is not selected, an argument of `AND` / `OR` after the deciding one) are discarded. The executor applies the remaining writes after the finalization, or drops them if the result is an Error.

### Formula models

`FormuLang.NET.Core.Model` (parameter specifications and their normalization, conversion of .NET values) and `FormuLang.NET.Extensions.Stock.Models` (definitions, instances, runs and live runs) implement the [formula models](https://github.com/Adisoreq/FormuLang/blob/main/docs/implementation/formula_models.md) of the standard, so host applications do not repeat the code of indicators, patterns and strategies. A definition is read from the metadata only and does not depend on an environment; a run creates the Stock context of its kind and uses the executor, the resumed iterations and the checks of `StockExecutor`, so a run gives the same results as `LOAD_INDICATOR` or `StockExecutor`. Display settings, the order model and storage stay in the host application.

### Parsing

`Parser` is a Pratt parser that follows [Core Parsing](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/semantics/parsing.md). Operators are translated while the tree is built, so the tree contains only these nodes:

| Node | Kind | Example key |
| :--- | :--- | :---------- |
| `LiteralNode` | Value | `4`, `"text"`, `true` |
| `VariableNode` | Variable | `$a`, `@array_indexing` |
| `ConstantNode` | Constant | `PI`, `ABS` (a function used as a Delegate) |
| `ParameterNode` | Parameter | `e` inside a lambda body |
| `CallNode` | FunctionCall | `ADD(POW($a,2),POW($b,2))` |
| `LambdaNode` | Lambda | `(e)=>MUL(e,2)`; its body is a separate `Sequence` |

The key of a node is its canonical form, used as the key of the primitive map.

### Evaluation

`Evaluator` traverses the parsing queue of every sequence bottom-up. The values are kept per node of the current queue, so a variable is read when its node is dequeued, as required by the standard. A call is resolved by name in the environment (`IEnvironment.GetFunction`); the first argument of `SET`, `INIT` and `COND_SET` is passed as a `VariableReference` instead of being read.

A lambda expression evaluates to a `Lambda` delegate that captures the scope it was created in, so its body can read context variables and the parameters of enclosing lambdas.

Functions receive the `IContext` of the execution, because the environment variables a formula changes (e.g. `@array_indexing = 1`) exist only in the context.

### Context types

The host application chooses the context a formula runs in, as in step 1 of [pre-execution](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/pre-execution.md); the formula does not select it. `Context` is the basic Core context (Default Context); `IterationContext` and `TimeContext` are the other Core context types, run with `Executor.ExecuteIterations` and `Executor.CreateFrameRunner`. The `NOW` constant is stored in the context in pre-execution step 2 (`Context.RefreshNow`), so it does not change during an execution: once per execution in the Default Context, once before the first iteration in the Iteration Context, once per frame in the Time Context. A context type with its own data (e.g. a stock indicator context with `CLOSE`, `N`, `I`) derives from it and overrides `LoadContextData()` (pre-execution step 2, run once per context). The host updates such constants between executions with `SetConstant`, which a formula cannot do. `Executor.Execute(formula, environment)` is only a shortcut for a new basic disposable context.

## Design rules

- **Errors are values.** Formula errors are returned as `IError` / `Value.Error(...)`, never thrown. Exceptions are reserved for programming errors of the host application (e.g. registering a function twice).
- **Abstractions first.** A new concept of the standard gets an interface in `Abstractions` before it gets an implementation in `Core`.
- **One standard, one module.** Code that belongs to an extension standard does not go into `Core`.
- **Standard identifiers come from the standard.** Error codes and names, operator spellings and binding powers, function signatures and standard identifiers (`core-1.0`) are copied from the standard documents; see [Development](development.md#keeping-the-library-in-sync-with-the-standard).
