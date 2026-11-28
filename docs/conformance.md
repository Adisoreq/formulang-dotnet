# Conformance

Status of the implementation against the FormuLang standard.

| | |
| :-- | :-- |
| **Target standards** | Core `core-1.3`, Stock `stock-1.3` (backward compatible with `core-1.0` - `core-1.2` and `stock-1.0` - `stock-1.2`), and the [formula models](https://github.com/Adisoreq/FormuLang/blob/main/docs/implementation/formula_models.md) |
| **Standard revision checked** | [`Adisoreq/FormuLang@f4dfc3e`](https://github.com/Adisoreq/FormuLang/tree/f4dfc3e) (2026-11-28) |
| **Last review** | 2026-11-28 |
| **Conformance tests** | [tests/FormuLang.NET.Conformance.Tests](../tests/FormuLang.NET.Conformance.Tests) (the JSON test cases of the standard: all 436 pass, except the known deviation D4; the cases of the earlier versions run too), [tests/FormuLang.NET.Core.Tests](../tests/FormuLang.NET.Core.Tests), [tests/FormuLang.NET.Extensions.Stock.Tests](../tests/FormuLang.NET.Extensions.Stock.Tests), with the examples of `1.3` in [Core13Tests.cs](../tests/FormuLang.NET.Core.Tests/Core13Tests.cs) and [Stock13Tests.cs](../tests/FormuLang.NET.Extensions.Stock.Tests/Stock13Tests.cs) |

Update the revision and this document every time the library is synced with a newer revision of the standard (see [Development](development.md)).

Legend: ✅ conforms · 🟡 partial / deviates · ❌ not implemented

## Pipeline

| Stage | Status | Notes |
| :---- | :----: | :---- |
| [Pre-execution](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/pre-execution.md) | ✅ | `Context.Create` / `PreExecute` copy only the missing environment variables, and set `NOW` once (from `Environment.TimeProvider`, or the `NOW` constant pinned with `Environment.SetNow`). |
| [Validation](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/semantics/validation.md) | ✅ | Environment checks (including `NOW` and `time_offset`) and the formula scan with the **Scoped comment** mode and Unicode text characters, with the error messages of the standard. Columns count code points. `Validator.ValidateEnvironment` checks only the environment (before the first hot execution in a context). |
| [Metadata reading](https://github.com/Adisoreq/FormuLang/blob/main/docs/core/metadata.md#metadata-reading) | ✅ | `MetadataReader.Read`, `Executor.ReadMetadata`; the metadata is a part of the prepared formula. See I28. |
| [Clean-up](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/cleanup.md) | ✅ | Scoped comments `<# ... #>` become one space, or one new line if they contain a line break. `Cleaner.CleanUpWithPositions` keeps the original position of every character. |
| [Tokenization](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/tokenization.md) | ✅ | Unicode characters are accepted only in the Text mode. Every token has its line and column (`IToken.Line`, `IToken.Column`). |
| [Parsing](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/semantics/parsing.md) | ✅ | See [Implementation choices](#implementation-choices) I8. A line that starts with a binary operator is a `SYNTAX_ERROR` with the message of the standard. |
| [Execution modes](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/execution.md#execution-modes) | ✅ | `Executor.Prepare` gives a `PreparedFormula` (an `IPreparedFormula`); `Executor.Evaluate`, `ExecuteIterations`, `ResumeIterations` and `CreateFrameRunner` accept it (a hot execution). A formula prepared for another environment, or before its functions changed, is prepared again. |
| [Warnings](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/semantics/warnings.md) | ✅ | `UNUSED_VALUE`, `CONTINUED_LINE`, `METADATA_TYPE`, `METADATA_CONTEXT` (`Warning`, `WarningCode`) in `PreparedFormula.Warnings` and `ExecutionResult.Warnings` (cold executions), with the line and column of the original formula; extension keys are checked with `Environment.RegisterMetadataChecker` (`MetadataChecks`). See I27, I29. |
| [Evaluation](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/evaluation.md) | ✅ | |
| [Finalization](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/finalization.md) | ✅ | `ExecutionResult`. |
| [Post-execution](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/post-execution.md) | ✅ | Handled by the host: a disposable context is dropped, a persistent one is passed to the next execution. |

## Core standard

| Area | Status | Notes |
| :--- | :----: | :---- |
| [Errors](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/errors/README.md) | ✅ | All 13 errors with the standard codes and names. |
| [Types](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/types/README.md) | ✅ | |
| [Serialization](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/semantics/serialization.md) | ✅ | `SerializationOptions`: `decimal_place`, `split_array`, `split_object`, `include_object_class`, `max_array_preview`, read from the environment variables of the execution (`TEXT`, `JOIN`, `ExecutionResult.ToString()`). |
| [Environment](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/README.md#environment-structure) | ✅ | `STANDARDS` is `["core-1.0", "core-1.1", "core-1.2", "core-1.3"]`, constants `STANDARDS`, `E`, `PI`, `PHI`, `NOW`, `NULL`, `TRUE`, `FALSE`, variables `array_indexing`, `suppress_errors`, `time_offset` and the serialization variables. |
| [Context types](https://github.com/Adisoreq/FormuLang/blob/main/docs/core/context.md#context-types) | ✅ | `Context` (Default Context), `IterationContext` (`I`, `N`, `PREV`; run with `Executor.ExecuteIterations`) and `TimeContext` (`TIME_S`, `TIME_MS`, `DTIME_S`, `DTIME_MS`, `FRAME`, `PREV`; run with `Executor.CreateFrameRunner` or `ExecuteFrames`). `I` and `FRAME` are computed when they are read (`ComputedConstant`). `NOW` is the same in all iterations and is set again for every frame. |
| [Memory](https://github.com/Adisoreq/FormuLang/blob/main/docs/core/context.md#memory), `HISTORY`, [resumed iterations](https://github.com/Adisoreq/FormuLang/blob/main/docs/core/context.md#resumed-iterations) | ✅ | `STORE`, `RECALL`, `CLEAR`, `ContextMemory` (`IContext.Memory`); `IterationContext.History`; `IterationContext.Extend`, `SaveState` / `RestoreState` and `Executor.ResumeIterations`. See I25, I31. |
| [Text](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/types/Text.md#characters) | ✅ | A character is one Unicode code point (`LEN`, `SUBSTR`, `FIND`, `REVERSE`, `SPLIT(text, "")`); `SORT` compares code points; `UPPERCASE` / `LOWERCASE` are invariant. Texts are not normalized. |
| [Comments](https://github.com/Adisoreq/FormuLang/blob/main/docs/syntax/operators.md#1-formula-and-line-structure) | ✅ | Line comments `#` and scoped comments `<# ... #>`. |
| [Inheritance](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/inheritance.md) of extended types | ✅ | |
| [Argument templates](https://github.com/Adisoreq/FormuLang/blob/main/docs/implementation/argument_template.md) and overloads | ✅ | Including element types (`Array<Number>`), the base types `X, Null` of `core-1.3` (`ArgumentTemplate.Nullable`, `IArgumentTemplate.Nullable`) and the error propagation rules. |
| [Procedures](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/procedures/README.md) | ✅ | `SET`, `INIT`, `COND_SET`, `REQUIRE`, `STORE`, `CLEAR`, also without parentheses. |
| [`AND` / `OR` short circuit](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/functions/AND.md#short-circuit) | ✅ | With 2 or more arguments (`ShortCircuitFunction`). All the arguments are still computed (as the standard requires until `core-2.0`); see I26. |
| [Missing values](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/README.md#missing-values) | ✅ | `core-1.3`: overloads for `null` of `GRE`, `GEQ`, `LES`, `LEQ` (`false`), `ADD`, `SUB`, `MUL`, `DIV`, `NEG`, `ABS` (`null`) and `GET` (a key of `null`); `MIN`, `MAX`, `SUM`, `AVG` with `null` elements, `MIN([])` and `MAX([])`. `NOT`, `IF`, `&&` and `\|\|` do not change. See I33. |
| [Functions](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/functions/README.md) | ✅ | All functions, including `FORMAT`, the time functions (`YEAR`, `MONTH`, `DAY`, `WEEKDAY`, `HOUR`, `MINUTE`, `SECOND`, `MILLISECOND`, `DATE`, `FORMAT_TIME`; years `1` - `9999`) and the functions of `core-1.1` (`CALL`, `FIRST`, `LAST`, `IFNULL`, `IFERR`, `ISINT`, `ISEVEN`, `ISODD`, `MEDIAN`, `VAR_P`, `VAR_S`, `STDEV_P`, `STDEV_S`, `MERGE`, the optional precision of `ROUND`, `FLOOR`, `CEIL`, `TRUNC`) and `RECALL` of `core-1.2`, and the functions of `core-1.3` (`APPEND`, `INSERT`, `PUT`, `REMOVE`, `VALUES`, `ENTRIES`, `FROM_ENTRIES`, `GET` with a fallback, `CONCAT` of arrays, `MERGE` of many objects; I34, I35), tested with the examples of their documents. |
| Lambda functions | ✅ | Delegate calls `$f(x)`, `f(x)`, `$o.f(x)` (`CALL`). The invocation depth is limited to 256 (`Lambda.MaxDepth`, per thread); a thread whose stack would overflow earlier gets an `OPERATION_ERROR` too (`RuntimeHelpers.TryEnsureSufficientExecutionStack`). |
| [Operators](https://github.com/Adisoreq/FormuLang/blob/main/docs/syntax/operators.md) | ✅ | |

## Stock standard

| Area | Status | Notes |
| :--- | :----: | :---- |
| [Context types](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/stock/README.md#context-structure) | ✅ | `StockContext`, `IndicatorContext`, `PatternContext` (with `PARAMETERS`), `StrategyContext`, derived from `IterationContext`. Expected results are checked by `StockExecutor.ExecuteIndicator`, `ExecutePattern`, `ExecutePatternSeries` and `ExecuteStrategy` (strategy signals are clamped). |
| `TIMEFRAME`, `INSTRUMENT`, `PARAMETERS` of a strategy, [signal object](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/stock/README.md#strategy-context) | ✅ | `StockContextOptions`; `StrategyContext.CompleteParameters` (done by `StockExecutor.ExecuteStrategy`); the signal object is checked in the finalization of the iteration (`StrategyContext.FinalizeResult`), so an invalid one is an `OPERATION_ERROR` of the iteration; `StrategyResult.SignalObjects`. |
| [Other timeframes](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/stock/README.md#other-timeframes) | ✅ | `StockContextOptions.Timeframes` (a dictionary) or `TimeframeProvider` (asked the first time a timeframe is used), `LOAD_CANDLES`, the options of `LOAD_INDICATOR` and `LOAD_PATTERN`; aligned series are stored. See I30. |
| [Series functions](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/stock/functions/README.md#series-functions), `SCORE`, `SIGNAL_SCORE` | ✅ | `READY`, `CROSSOVER`, `CROSSUNDER`, `HIGHEST`, `LOWEST`, `RISING`, `FALLING`, `BARS_SINCE`, `VALUE_WHEN`, `SCORE` (with `null` conditions since `stock-1.3`), `SIGNAL_SCORE` (`stock-1.3`, I36) (`SeriesFunctions`). |
| [Formula results](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/stock/README.md#formula-results) | ✅ | `stock-1.3` summarizes the results of `1.2`, which do not change: a signal object is checked in the finalization of the iteration (an invalid one is an `OPERATION_ERROR` of the iteration); a result of another shape (a Text of an indicator or a strategy, a Number of a pattern) is reported by `StockExecutor` for the whole run, as in `1.2`. See the inconsistencies. |
| [Metadata](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/stock/README.md#metadata) | ✅ | Check of the `stock` key (`StockStandard.MetadataChecker`); registry definitions from the metadata (`FormulaRegistry.AddFormula`, `FormulaDefinition.FromFormula`); identifiers may contain `-` (`FormulaIds.IsValid`). |
| [Formula models](https://github.com/Adisoreq/FormuLang/blob/main/docs/implementation/formula_models.md) | ✅ | `ParameterSpec`, `Parameters.Normalize` and `Parameters.Key` (Core, `FormuLang.NET.Core.Model`); definitions, instances, runs and live runs (`FormuLang.NET.Extensions.Stock.Models`); the cases of `tests/stock/models.json` pass. See I32. |
| [Formula registry](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/stock/README.md#2-formula-registry) | ✅ | `FormulaRegistry` with the parts `Indicators` and `Patterns` (identifiers unique within a part), held by the `LOAD_INDICATOR` and `LOAD_PATTERN` functions of the environment (`RegisterStock`, `GetFormulaRegistry`). |
| [`Stock.Candle`](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/stock/types/Candle.md) | ✅ | `Candle` record; invalid candles of the host are rejected with `ArgumentException`. |
| [Functions](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/stock/functions/README.md) | ✅ | `CANDLE`, `IS_BULLISH`, `IS_BEARISH`, `LOAD_INDICATOR`, `LOAD_PATTERN`, `LOAD_CANDLES`, `LAG`, `WINDOW`, the series functions, `SCORE` and `SIGNAL_SCORE`. Both loaders store every computed series (key: timeframe, identifier, completed parameters and, for indicators, the source), as allowed by their *Performance* sections. A child context uses the `NOW` of its caller. |
| [Examples](https://github.com/Adisoreq/FormuLang/tree/main/docs/standards/stock/examples) | ✅ | SMA, EMA, MACD, RSI with the memory, bullish engulfing, moving average crossover (also with `CROSSOVER` and `PARAMETERS`), MACD signal strength, pattern confirmed by a trend, signal objects with `SCORE`, the trend of another timeframe, and the `stock-1.3` strategies without warm-up checks (examples 8 and 9). |

## Known deviations

Remove an item when it is fixed and covered by a test.

| # | Deviation | Standard |
| :-: | :-------- | :------- |
| D3 | Resolved: conformance case `core.syntax.unicode.011` expected one error for `$zółw = 1`, but step 3.3 of the validation algorithm reports every invalid symbol. The standard fixed the case (it expects both errors), so it is no longer a known deviation. | [tests/core/syntax/text_unicode.json](https://github.com/Adisoreq/FormuLang/blob/main/tests/core/syntax/text_unicode.json) |
| D4 | Conformance case `stock.context.005` returns `PARAMETERS` (`{ }`) as the result of a strategy and expects `{ }`, but an Object without `signal` is an `OPERATION_ERROR` of the iteration ([Strategy Context](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/stock/README.md#strategy-context)), as case `stock.strategy.002` checks. The library follows the rule (as FormuLang-TS does); the case should read e.g. `LEN(KEYS(PARAMETERS))` with the result `0`. | [tests/stock/stock12.json](https://github.com/Adisoreq/FormuLang/blob/main/tests/stock/stock12.json) |
| D2 | The primitive map holds the classified expressions; the values computed during evaluation are kept per node of the current queue instead of being written into the map. The order of evaluation and the results are the same. | [evaluation.md § Result propagation](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/evaluation.md#4-result-propagation) |

## Implementation choices

Places where the standard leaves room for interpretation, and what the library does. Each is a candidate for clarification in the standard.

| # | Topic | Choice |
| :-: | :---- | :----- |
| I1 | A character that can begin a 2-character operator (`=`, `:`, `<`, `!`, ...) is also listed as a single-character operator, so step 2.2.6.1 of the tokenization algorithm would close it before the 2-character form is seen. | The tokenizer keeps the token open while it can still become a 2-character operator. |
| I2 | An operator token whose spelling is not in the operator table (`&`, `\|`, `?`, `~`, `'`, `` ` ``, `\` outside text). | `SYNTAX_ERROR` during tokenization, because a token must have a supported spelling ([token.md](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/token.md)). |
| I3 | A number token that is not a valid literal (`1_`, `1e`, `0x`). | `SYNTAX_ERROR` when the token is closed. |
| I4 | Changing a constant: the Core standard defines `ENVIRONMENT_ERROR` for environment constants ([SET](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/procedures/SET.md)), but no error for other constants. | `ENVIRONMENT_ERROR` for every constant. |
| I5 | `TRUE`, `FALSE` and `NULL` are described as environment constants in [literals.md](https://github.com/Adisoreq/FormuLang/blob/main/docs/syntax/literals.md), but are missing from the Core environment table. | They are defined in `Environment.Create()`. |
| I6 | Serialization of numbers with `decimal_place = -1` is limited by `double`. | Shortest round-trip decimal notation, invariant culture. |
| I7 | Clean-up of an empty or white-space-only formula. | `OPERATION_ERROR`. |
| I8 | `{ k: v }` is translated to `OBJECT("k",v)` ([Core parsing](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/semantics/parsing.md#operator-translation)), but `OBJECT` is not in the Core functions index. | `OBJECT(...entries? <Any>)` is a Core function taking key-value pairs. |
| I9 | An identifier without a sigil that names a function (`MAP($a, ABS)`, `TEXT(AVG)`). | It is a Delegate of that function. Constants are checked first. |
| I10 | The README of the standard calls a lambda stored in a variable as `$square(5)`. | Supported since `core-1.1`: a delegate call is translated into `CALL`. |
| I11 | `NUMBER(text)`: the standard does not say whether a sign is allowed (`NUMBER("-5")`). | A leading `+` or `-` is accepted. |
| I13 | `NOW` is an environment constant set in the pre-execution, but an environment can be shared by many executions. | The value is stored as a constant of the context (`Context.Now`), which shadows the constant of the environment, so concurrent executions do not change the environment. |
| I14 | A placeholder selector of `FORMAT` that is a whole number with a sign (`{-1}`). | It is an index (and results in `INVALID_INDEX_ERROR` unless `@array_indexing` allows it). |
| I12 | Lambda body scope: the standard does not say whether a body can read context variables. | It can (`MAP($arr, (e) => e - AVG($arr))` from the [common formulas](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/examples/common_formulas.md) needs it), and it can read the parameters of enclosing lambdas. |
| I13 | Choice of the context type. | The host application chooses and creates the context ([pre-execution](https://github.com/Adisoreq/FormuLang/blob/main/docs/semantics/pre-execution.md), step 1); a formula cannot select it. A context type is a class derived from `Context` that loads its constants in `Context.LoadContextData()`. `ContextType` (in Abstractions) is the context *kind*: disposable or persistent. |
| I14 | `PREV` after an iteration that resulted in an Error, when the host continues ([context.md](https://github.com/Adisoreq/FormuLang/blob/main/docs/core/context.md#2-iteration-context)). | `null`. `ExecuteIterations(..., stopOnError: false)` continues; the default stops at the first Error. |
| I15 | A formula of an Iteration Context that does not parse. | It fails once: `ExecuteIterations` returns a single failed result. |
| I16 | Characters of a text (`LEN`, `SUBSTR`, `FIND`, `REVERSE`). | UTF-16 code units, the same as `SPLIT(text, "")`. |

The choices of `core-1.2`, `stock-1.2`, the formula models, `core-1.3` and `stock-1.3` are the same as in FormuLang-TS 1.3.0 (its numbers I25 - I36), so both implementations give the same results:

| # | Topic | Choice |
| :-: | :---- | :----- |
| I25 | Memory writes in an `IF` whose condition is not a Boolean (no branch is selected), and in `AND` / `OR` with an Error before the deciding argument. | `IF`: the writes of both branches are discarded. `AND` / `OR`: the writes of the arguments up to the first deciding value are kept, the others discarded. A write is attributed to the call node that made it, also through a lambda the call invokes. |
| I26 | `AND(true, 1, false)`: rule 2 of the short circuit applies when no **Error** is before the first `false`, also with a value that is not a Boolean before it. | `false` (and `OR(false, 1, true)` is `true`). With one argument, nothing is decided: `ARGUMENT_ERROR`. |
| I27 | `UNUSED_VALUE` looks only at the root of a sequence. | Literal: `IF(I == 2, CLEAR(), 0)` and a declaration `$x` that are not the last sequence get the warning; a call of an unknown function does not (it is an Error of the evaluation). `CONTINUED_LINE` needs the previous sequence to end on the line before. |
| I28 | The metadata block is replaced with one new line or one space. | Every character of the block is replaced with a space (line breaks stay), so the lines and columns of the rest of the formula do not change; the result is the same, because the clean-up removes empty lines and repeated spaces. |
| I29 | Messages of `METADATA_TYPE` for nested keys and for `result` / `requires`. | Dotted keys (`parameters.period.min`, `stock.outputs.macd.name`) and the type `Text or Array of Text`. The pattern of `id` is not checked by the warnings. |
| I30 | Other timeframes: the timeframe of the main chart, the length of the main timeframe, nested loads. | A timeframe equal to `TIMEFRAME` is the main chart (identity alignment). Other timeframes need `TimeframeLength`, and their candles need `time`: otherwise an `ArgumentException` for the host application. A child context of another timeframe can load the timeframes of the same host. |
| I31 | Resumed iterations of a Stock Context. | `StockContext.AppendCandles` adds candles; the series loaded by `LOAD_INDICATOR`, `LOAD_PATTERN` and `LOAD_CANDLES` are computed again. `Extend` of a Stock Context is an `InvalidOperationException`. |
| I32 | [Formula models](https://github.com/Adisoreq/FormuLang/blob/main/docs/implementation/formula_models.md): the given values of the host application. | .NET values and `JsonElement`s are converted with `ValueConversion`; a value that cannot be converted (e.g. a delegate) is a `TYPE` problem. A live indicator run with a `SOURCE` of the host needs its values of every new candle (`LiveIndicatorRun.Append(candles, source)`). |
| I33 | `MIN` / `MAX` with a handler that returns `null` for one element: the algorithm says "if a call returns `null`, the result is `null`", but not whether the next elements are still computed. | Every element is computed (as without `null`), so an Error or a result that is neither a Number nor `null` after a `null` result is still returned; otherwise the result is `null`. |
| I34 | `MERGE` with one object: overload 2 is "`MERGE(...objects)`, with more than two arguments". | Overload 2 is `MERGE(object1, object2, ...objects)`, so `MERGE({ a: 1 })` is still an `ARGUMENT_ERROR`, as in `core-1.2`. |
| I35 | The order of the checks of `FROM_ENTRIES`, and arguments that are Errors in the new functions. | `FROM_ENTRIES` checks every element first (an Error element is returned, an element that is not a pair with a Text key is an `ARGUMENT_ERROR`), then the keys (`INVALID_KEY_ERROR`). The `value` of `APPEND`, `INSERT`, `PUT` and the `fallback` of `GET` are of any type, an Error included, like the elements of `ARRAY`: `GET({ a: 1 }, "a", ERROR("x"))` is `1`. `GET(null, key, fallback)` matches no overload. The messages of the new errors are the library's own. |
| I36 | `SIGNAL_SCORE` with weights: the order of the errors, and the weight of a key that both sides have. | First the keys of `weights` that are keys of neither side (`INVALID_KEY_ERROR`), then `SCORE` of the buy side, then of the sell side, each with the weights of its own keys (a key of both sides gets its weight on both). The messages name `SIGNAL_SCORE`. |

## Inconsistencies found in the standard

Found while writing the conformance tests. The tests follow the algorithms, not these examples.

| Document | Problem |
| :------- | :------ |
| [number_examples.md](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/examples/number_examples.md), example 4 | The formula uses `$c = 2.5e+2` (250), but the table and the result `-21` use `2.5e-1` (0.25). |
| [array_examples.md](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/examples/array_examples.md), [text_examples.md](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/examples/text_examples.md) | Results are written as `[ [ 1, 2, 3 ], [ a, b, c ] ]`; [serialization](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/semantics/serialization.md) gives `[[1, 2, 3], ["a", "b", "c"]]`. |
| [context_examples.md](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/examples/context_examples.md), example 6 | The result is written as `TYPE_ERROR: ...` instead of `[TYPE_ERROR (4)] ...`. |
| [environment_examples.md](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/examples/environment_examples.md), example 4 | `REQUIRE "math-1.x"` fails in a Core environment, so the shown result `12.5663706144` is never reached. |
| [README.md](https://github.com/Adisoreq/FormuLang/blob/main/README.md) of the standard | `$squared = $square(5)` (see I10). |
| [Core README](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/README.md), [Core CHANGELOG](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/CHANGELOG.md), [standards list](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/README.md) | The commit `87614e0` is "Core version 0.3", but the documents still give the identifier `core-0.2`, and its changes are listed under `0.2`. The library uses `core-0.3`. |
| [environment_examples.md](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/examples/environment_examples.md), example 3 | `REQUIRE "core-0.2"` (exact version) fails with `core-0.3`; the test uses `core-0.3`. |
| [Core CHANGELOG](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/CHANGELOG.md#13), [Missing values](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/README.md#missing-values) | "`null \|\| x` is still an `ARGUMENT_ERROR`", but by the short circuit of `core-1.2` ([OR.md](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/core/functions/OR.md#short-circuit), rule 2) `null \|\| true` is `true`, and `null && false` is `false`; only `null \|\| false` and `null && true` are an `ARGUMENT_ERROR`. The library follows `OR.md` and `AND.md` (I26), so these results do not change. |
| [Stock README](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/stock/README.md#formula-results), Formula results | "A result of another shape is an `OPERATION_ERROR` of the iteration", but the Indicator and Pattern Contexts say that the result *of the indicator* (of the pattern) is an `OPERATION_ERROR`, and the Stock changelog says that the results do not change in `1.3`. The library keeps the rules of `1.2`: only an invalid signal object is an Error of the iteration. |
