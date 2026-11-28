<div align="center">

<img src="./raw/images/icons/monogram.svg" height="50" alt="FormuLang.NET monogram">

# Changelog

</div>

All notable changes of **FormuLang.NET**. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); the library uses [Semantic Versioning](https://semver.org).

## [1.3.0] - 2026-11-28

**Supported standards:** `core-1.3`, `stock-1.3` ([`Adisoreq/FormuLang@f4dfc3e`](https://github.com/Adisoreq/FormuLang/tree/f4dfc3e); backward compatible with `core-1.0` - `core-1.2` and `stock-1.0` - `stock-1.2`). Formulas whose result is a value in `1.2` give the same value; only formulas whose result was an Error can give a value now (also inside `IFERR` and `ISERR`). The choices that the standard leaves open are the same as in FormuLang-TS 1.3.0 ([Conformance](docs/conformance.md#implementation-choices), I33 - I36).

### Added

- `core-1.3` missing values: `<`, `<=`, `>`, `>=` (`LES`, `LEQ`, `GRE`, `GEQ`) with `null` on either side are `false`; `+`, `-`, `*`, `/`, unary `-` (`ADD`, `SUB`, `MUL`, `DIV`, `NEG`) and `ABS` with `null` are `null` (`null / 0` too); a key of `null` (`null.close`, `GET(null, key)`) is `null`. `==` and `!=` do not change, and `NOT(null)`, `IF(null, a, b)` and `null && true` are still an `ARGUMENT_ERROR`.
- `core-1.3` `GET(array, index, fallback)` and `GET(object, key, fallback)`; the operator `array[index]` still gives `INVALID_INDEX_ERROR`.
- `core-1.3` functions that return a copy and never change their arguments: `APPEND`, `INSERT`, `PUT` and `REMOVE` (of arrays and of objects), `VALUES`, `ENTRIES`, `FROM_ENTRIES`, `CONCAT(array1, array2)` and `MERGE` of more than two objects.
- `stock-1.3` `SIGNAL_SCORE(buy, sell, weights?)`: `SCORE(buy) - SCORE(sell)` from `-1` to `1`, of Objects or Arrays, with one Object of weights for the keys of both sides (`SeriesFunctions.SignalScore`, `SeriesFunctions.SignalScoreName`).
- Argument templates that also accept `null` (`Number, Null` of the standard): `ArgumentTemplate.Nullable` (the `nullable` parameter of `ArgumentTemplate.Of` and `ArgumentTemplate.ArrayOf`) and `IArgumentTemplate.Nullable` (a default interface member, `false`, so existing implementations still compile); `ToString()` gives `<Number | Null>` and `<Array<Number | Null>>`.
- WebPreview: the examples _Missing values_ (Default Context) and _Buy and sell conditions_ (Strategy Context, `SIGNAL_SCORE` without warm-up checks).
- Tests of the `1.3` examples of the standard that the JSON conformance tests do not cover (`Core13Tests`, `Stock13Tests`).

### Changed

- `core-1.3`: `MIN`, `MAX`, `SUM` and `AVG` of an array with a `null` element are `null` (not skipped), also with a handler of `MIN` / `MAX` that returns `null`; `MIN([])` and `MAX([])` are `null` instead of an `OPERATION_ERROR`. `AVG([])` is still an `OPERATION_ERROR`.
- `stock-1.3`: `SCORE` accepts `null` conditions, counted as conditions that are not met (`SCORE([true, null])` is `0.5`).
- The version of the standards is `core-1.3` and `stock-1.3`; `STANDARDS` is `["core-1.0", "core-1.1", "core-1.2", "core-1.3"]` (and `"stock-1.0"` - `"stock-1.3"`). `RegisterStock` requires `core-1.3+`; `CoreStandard.CompatibleIdentifiers` and `StockStandard.CompatibleIdentifiers` list the `1.2` versions too.
- The conformance tests are synced with `Adisoreq/FormuLang@f4dfc3e` (`core13.json`, `stock13.json`): all 436 cases pass, except the known deviation D4.

## [1.2.0] - 2026-11-27

**Supported standards:** `core-1.2`, `stock-1.2` ([`Adisoreq/FormuLang@1e7b2a6`](https://github.com/Adisoreq/FormuLang/tree/1e7b2a6); backward compatible with `core-1.0`, `core-1.1`, `stock-1.0` and `stock-1.1`), and the formula models of the standard. The choices that the standard leaves open are the same as in FormuLang-TS 1.2.0 ([Conformance](docs/conformance.md#implementation-choices), I25 - I32).

### Added

- Formula models ([FormuLang docs/implementation/formula_models.md](https://github.com/Adisoreq/FormuLang/blob/main/docs/implementation/formula_models.md)), so host applications do not repeat the code of indicators, patterns and strategies:
  - Core (`FormuLang.NET.Core.Model`): `ParameterSpec` (read from the `parameters` of the metadata), `Parameters.Normalize` (defaults, types, `min` / `max`, `options`, unknown keys; `ParameterProblem`, `ParameterProblemCode`), `Parameters.Key`, and `ValueConversion` between .NET values, `JsonElement`s and FormuLang values.
  - Stock (`FormuLang.NET.Extensions.Stock.Models`): `Definition.FromFormula` / `TryFromFormula` with `IndicatorDefinition` (outputs), `PatternDefinition` and `StrategyDefinition` (labels of the checks), `InvalidDefinitionException` with its reason; `Definition.Normalize` and `CreateInstance` (FormuLang values, .NET values or a `JsonElement`; `ParameterException`), `Instance.ToJson` / `Definition.InstanceFromJson`; `Compute(environment, chart)` gives the `IndicatorSeries` (outputs by key), the `PatternSeries` or the `StrategySignals` (`StrategySignal` with `Reason`, `Checks` and `Extra`); `Live(...)` with `Start`, `Append` (resumed iterations) and `SaveState` / `RestoreState`; `FormulaRegistry.AddDefinition`.
  - Identifiers of the registry may contain `-` (`FormulaIds.IsValid`), like the `id` of the metadata: `registry.Indicators.Add("macd-ema200", ...)`, `LOAD_INDICATOR("macd-ema200")`.
- `core-1.2` memory of the Iteration and Time Contexts: the procedures `STORE(key, value)` and `CLEAR(key)` / `CLEAR()` and the function `RECALL(key, fallback?)`. The writes are applied after the iteration (`ContextMemory.Commit`), only on the selected path of `IF`, `AND` and `OR`, and only if the iteration did not result in an Error. `IContext.Memory`, `IMemory`, `ContextMemory` (`Entries`, `Load` for the host application), `Procedure.WritesMemoryOnly`.
- `core-1.2` `HISTORY` constant of the Iteration Context (`IterationContext.History`).
- `core-1.2` short circuit of `AND` / `OR` (and `&&` / `||`): the first `false` / `true` decides the result, and the arguments after it are discarded; `AND(...values)` and `OR(...values)` with more than two arguments (`ShortCircuitFunction`). `Function.Invoke` is virtual.
- `core-1.2` metadata blocks `<@ { ... } @>`: `MetadataReader.Read`, `Executor.ReadMetadata`, `MetadataResult`, `IMetadataResult`; the metadata is a part of the prepared formula.
- `core-1.2` warnings `UNUSED_VALUE`, `CONTINUED_LINE`, `METADATA_TYPE`, `METADATA_CONTEXT` (`IWarning`, `Warning`, `WarningCode`, `Warnings`), in `PreparedFormula.Warnings` and `ExecutionResult.Warnings`, with the line and column of the original formula; checks of extension keys with `Environment.RegisterMetadataChecker` (`MetadataChecker`, `MetadataChecks`). A line that starts with a binary operator is a `SYNTAX_ERROR` with the message of the standard.
- `core-1.2` execution modes: `Executor.Prepare` returns a `PreparedFormula` (an `IPreparedFormula`), `Executor.Evaluate(prepared, context)` is a hot execution; `ExecuteIterations`, `ResumeIterations` and `CreateFrameRunner` accept a prepared formula.
- `core-1.2` resumed iterations: `IterationContext.Extend(count)`, `CompletedIterations`, `LastResult`, `SaveState` / `RestoreState` (`IterationState`), `Executor.ResumeIterations`; `StockContext.AppendCandles`, `IndicatorContext.AppendCandles(candles, source)`.
- `Context.ContextTypeNames` and `Context.FinalizeResult`; `IToken.Line` / `Column`; `Cleaner.CleanUpWithPositions` (`CleanUpPositions`, `SourcePosition`); `Validator.ValidateEnvironment`; `Environment.FunctionCount`.
- `stock-1.2` constants `TIMEFRAME` and `INSTRUMENT`, and `PARAMETERS` in the Strategy Context (`StrategyContext.Create(environment, candles, options, parameters)`, `CompleteParameters`, `ParameterDefaults`).
- `stock-1.2` signal objects `{ signal, reason?, checks?, ... }` of strategies (`StrategyResult.SignalObjects`, `StrategyContext.SignalObjectProblem`).
- `stock-1.2` other timeframes: `StockContextOptions` (`Timeframe`, `Instrument`, `TimeframeLength`, `Timeframes`, `TimeframeProvider`), `TimeframeCandles`, `LOAD_CANDLES`, the options overloads of `LOAD_INDICATOR` (`source`, `timeframe`, `output`, `align`) and `LOAD_PATTERN` (`timeframe`, `align`).
- `stock-1.2` series functions `READY`, `CROSSOVER`, `CROSSUNDER`, `HIGHEST`, `LOWEST`, `RISING`, `FALLING`, `BARS_SINCE`, `VALUE_WHEN`, and the strategy function `SCORE` (`SeriesFunctions`).
- `stock-1.2` metadata: the check of the `stock` key (`StockStandard.MetadataChecker`), `FormulaDefinition.FromFormula` and `FormulaRegistry.AddFormula` (definitions read from the metadata).
- `StockExecutor` accepts prepared formulas; `StockExecutor.IndicatorResultsError`, `PatternResultError`, `StrategyResultError`.
- WebPreview: Core 1.2 and Stock 1.2 examples (metadata and warnings, short circuit, memory, `HISTORY`, distance, series functions, weekly candles, Wilder's RSI with the memory, `CROSSOVER`, `SCORE` with a signal object, the weekly trend), the warnings and the metadata of every formula, `TIMEFRAME` = `"1d"`, `INSTRUMENT` = `"MOCK"` and the weekly candles `"1w"`, and the last signal object of the selected strategy.

### Changed

- The version of the standards is `core-1.2` and `stock-1.2`; `STANDARDS` is `["core-1.0", "core-1.1", "core-1.2"]` (and `"stock-1.0"`, `"stock-1.1"`, `"stock-1.2"`). `RegisterStock` requires `core-1.2+` and registers the check of the `stock` metadata key.
- The execution steps have the metadata reading as step 2. The clean-up of the `Executor` keeps the positions of the characters, so the tokens have the lines and columns of the original formula.
- `StockExecutor.ExecuteStrategy` accepts signal objects; the error of another result says "neither a Number nor a signal object".
- The key of a stored series of `LOAD_INDICATOR` and `LOAD_PATTERN` starts with the timeframe.
- The identifier of a registry definition follows the rule of the `id` of the metadata (`FormulaIds.IsValid`) instead of the rule of an identifier token.

## [1.1.0] - 2026-11-25

Supported standards: `core-1.1`, `stock-1.1` (backward compatible with `core-1.0` and `stock-1.0`). Faster execution of formulas run for every candle or element (Iteration Context); the results of `1.0` formulas are the same.

### Added

- `core-1.1`: delegate calls `$f(x)`, `f(x)` for a lambda parameter, `$o.f(x)`, `$a[0](x)`, `((x) => x)(1)`, translated by the parser into the new function `CALL`; the invocation depth is limited to 256 (`Lambda.MaxDepth`) and a too deep invocation is an `OPERATION_ERROR` instead of a `StackOverflowException`.
- `core-1.1` functions: `FIRST`, `LAST`, `IFNULL`, `IFERR`, `ISINT`, `ISEVEN`, `ISODD`, `MEDIAN`, `VAR_P`, `VAR_S`, `STDEV_P`, `STDEV_S`, `MERGE`; the `precision` of `ROUND`, `FLOOR`, `CEIL` and `TRUNC` is optional.
- `stock-1.1` functions: `LAG(series, offset?)` and `WINDOW(series, length, handler?)`.
- `STANDARDS` lists the earlier compatible versions too (`["core-1.0", "core-1.1"]`, then `"stock-1.0"`, `"stock-1.1"`), so `REQUIRE "core-1.0"` keeps working (`CoreStandard.CompatibleIdentifiers`, `StockStandard.CompatibleIdentifiers`).
- `Function.SideEffectFree`: a function that only computes its result. All Core and Stock functions are side-effect free; functions of the host application are not, unless they set it.
- `Value.Number(double)`: a Number whose expression is formatted when it is first read.

### Changed

- The evaluator skips the branch of `IF` that is not selected when every function of the sequence (or of the value of an assignment, e.g. `$x = IF(...)`) is side-effect free and the condition is a **Boolean**. The selected value and the errors are the same as with both branches computed; a function with side effects, a procedure or a delegate from a variable keeps the computation of both branches. An indicator such as RSI or EMA with a first value computed by `IF(ISNULL(PREV), ...)` runs about 2.5-4 times faster.
- `IterationContext.BeginIteration` runs the pre-execution once instead of twice, keeps the copies of the environment variables that the formula did not change, and reuses the `PREV` constant: about 40% less work per iteration.
- `LOAD_INDICATOR` checks the elements of a source once for every array (never for `CLOSE`) instead of in every call, and compares the elements of a source as numbers instead of serializing them to text, to find a series that is already computed (e.g. a source built with `MAP` in every iteration).
- A Number result of a Core function gets its expression (the serialized number) when it is first read, not when it is created (`Value.Number(double)`).

## [1.0.1] - 2026-11-23

Supported standards: `core-1.0`, `stock-1.0`. The behavior of the library is the same as in `1.0.0`.

### Fixed

- The library did not build with the .NET 10 SDK: `REVERSE` called `array.Reverse()`, which C# 14 binds to the in-place `MemoryExtensions.Reverse(Span<T>)` instead of `Enumerable.Reverse`. It now calls `Enumerable.Reverse` explicitly.
- `Directory.Build.props` no longer sets `<LangVersion>latest</LangVersion>`, so the C# version follows the target framework (C# 12 for `net8.0`) with any SDK.

## [1.0.0] - 2026-11-23

First release, published on NuGet as `FormuLang.NET.Abstractions`, `FormuLang.NET.Core` and `FormuLang.NET.Extensions.Stock`. Supported standards: `core-1.0`, `stock-1.0` (see [Conformance](docs/conformance.md), standard revision `Adisoreq/FormuLang@c40e08a`).

### Added

- `core-1.0`: scoped comments `<# ... #>` (clean-up and the **Scoped comment** mode of the validation); Unicode characters in text literals, with text functions that count Unicode code points (`LEN`, `SUBSTR`, `FIND`, `REVERSE`, `SPLIT(text, "")`) and `SORT` by code points.
- `core-1.0` functions: `FORMAT(template, values)` and the time functions `YEAR`, `MONTH`, `DAY`, `WEEKDAY`, `HOUR`, `MINUTE`, `SECOND`, `MILLISECOND`, `DATE`, `FORMAT_TIME`.
- Environment constant `NOW` (set once per execution in the pre-execution, from `Environment.TimeProvider`; `Environment.SetNow` pins it) and environment variable `time_offset`, both checked by the validation. `Context.Now`, `Context.FixedNow`.
- `TimeContext` (Core Time Context with `TIME_S`, `TIME_MS`, `DTIME_S`, `DTIME_MS`, `FRAME`, `PREV`), `Executor.CreateFrameRunner`, `FrameRunner` and `Executor.ExecuteFrames`.
- `stock-1.0`: `IS_BULLISH`, `IS_BEARISH`, `LOAD_PATTERN`, `PARAMETERS` in the Pattern Context (`PatternContext.Create(..., parameters)`), `StockContext.ComputePattern`, `StockExecutor.ExecutePatternSeries`.
- Test project `FormuLang.NET.Conformance.Tests`: runner of the language-independent JSON conformance tests of the standard, copied by `scripts/sync-conformance.ps1`.
- NuGet packaging (`Directory.Build.props`, package READMEs and icon, XML documentation, symbols, SourceLink) and the GitHub Actions workflows `ci.yml` (build, test, pack) and `release.yml` (publishes the packages for a `v*` tag).
- Samples: CalculatorCLI supports the Stock standard, Unicode input and multi-line scoped comments in the interactive mode, with new examples (`time.fl`, `text.fl`, `candles.fl`). WebPreview has the Time Context (frames played as time passes), patterns in the formula registry with registry ids and parameters, and examples of the new functions.

### Changed

- **Breaking:** the Core identifier is `core-1.0` and the Stock identifier `stock-1.0`; Stock requires `core-1+`.
- **Breaking:** `INDICATOR` is renamed `LOAD_INDICATOR` (`StockFunctions.LoadIndicatorName`, `LoadIndicatorFunction`).
- **Breaking:** `IndicatorRegistry`, `IndicatorDefinition` and `IndicatorParameter` (namespace `Extensions.Stock.Indicators`) are replaced with `FormulaRegistry` (parts `Indicators` and `Patterns`), `FormulaDefinition` and `FormulaParameter` (namespace `Extensions.Stock.Registry`); `GetIndicatorRegistry` is `GetFormulaRegistry`.
- `IndicatorContext.Caller` moved to `StockContext.Caller`.

### Known deviations

- Conformance case `core.syntax.unicode.011` expects one validation error for `$zółw = 1`; the library reports one for every invalid symbol, as the validation algorithm says (see [Conformance](docs/conformance.md#known-deviations), D3).

### Added in the development versions (core-0.3, stock-0.1)

- Core `core-0.3` functions: `SIGN`, `TRUNC`, `SUBSTR`, `FIND`, `CONTAINS`, `STARTS`, `ENDS`, `REPLACE`, `REVERSE`, `LEN(text)`, `SEQ`, `REDUCE`, `ANY`, `ALL`, `SORT`, `HAS`, `KEYS` and the trigonometric functions `SIN`, `COS`, `TAN`, `ASIN`, `ACOS`, `ATAN`, `ATAN2`, `DEG`, `RAD`. `MAP` accepts a lambda with an index, `(e, i) => ...`.
- Serialization variables `decimal_place`, `split_array`, `split_object`, `include_object_class`, `max_array_preview` in the environment; `SerializationOptions` and `StringConverter.ValueToString(value, options)`. `TEXT`, `JOIN` and `ExecutionResult.ToString()` use the variables of the execution.
- Validation of the constants `NULL`, `TRUE`, `FALSE` and of the serialization variables.
- `IterationContext` (Core Iteration Context with `I`, `N`, `PREV`) and `Executor.ExecuteIterations`, which parses the formula once and evaluates it for every iteration. `ComputedConstant` for constants computed when they are read.
- `Object.Keys` keeps the order in which the keys were defined; `Object.SetContainer`.
- `AbstractTree` and `AbstractTreeNode` are public, so extension standards can define their type trees.
- `FormuLang.NET.Extensions.Stock` (`stock-0.1`): `Candle` / `Stock.Candle`, `CANDLE`, `INDICATOR` with an `IndicatorRegistry`, `StockContext`, `IndicatorContext`, `PatternContext`, `StrategyContext`, `StockExecutor` and `Environment.RegisterStock()`. Test project `FormuLang.NET.Extensions.Stock.Tests`.

- Complete execution pipeline: `Validator` (all problems at once), `Parser` (Pratt parser with operator translation, parsing queues and primitive map), `Evaluator` (variable reading rules, sequence results, `suppress_errors`, lambdas), and `Executor`, which runs every stage and returns an `ExecutionResult`.
- `Context` with pre-execution (copies of environment variables), disposable and persistent contexts.
- All Core functions (`CoreFunctions`), the `OBJECT` function for object literals, and the procedures `SET`, `INIT`, `COND_SET`, `REQUIRE` (`CoreProcedures`); `CoreStandard`.
- Element types in argument templates (`ArgumentTemplate.ArrayOf`, `Array<Number>`).
- `Environment.RegisterFunction` and `RegisterStandard(IStandard)`; `Object.RemoveProperty`.
- Custom context types: `Context.LoadContextData()` (pre-execution step 2, run once per context) and `Object.SetConstant` for constants updated by the host, e.g. the iteration of a loop context.
- `CalculatorCLI` executes formulas (argument, file, standard input or interactive mode) and prints the result or the error.
- `FormuLang.NET.Abstractions`: interfaces of primitives, functions, operators, pipeline stages and structures.
- `FormuLang.NET.Core`: primitives (`Value`, `Variable`, `Constant`, `Object`, `Error`), Core errors, operator table, clean-up and tokenization.
- `Types` factory for base and extended types, `Value.Delegate`, `Value.Object` with an extended type.
- Public `ArgumentTemplate` and `FunctionOverload` constructors, so functions can be defined outside the library.
- `Environment.RegisterStandard` and `Environment.RegisterExtendedType`.
- `NumberValidator.TryParse` for all Number literal notations.
- `Associativity` of operators; `OperatorType.Boundary` and `OperatorType.ItemSeparator`.
- Conformance test project `FormuLang.NET.Core.Tests`.
- Documentation: README, getting started, architecture, conformance, development guide.
