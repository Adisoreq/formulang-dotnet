<div align="center">

<img src="./raw/images/icons/monogram.svg" height="50" alt="FormuLang.NET monogram">

# Changelog

</div>

All notable changes of **FormuLang.NET**. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); the library uses [Semantic Versioning](https://semver.org).

## [Unreleased]

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
