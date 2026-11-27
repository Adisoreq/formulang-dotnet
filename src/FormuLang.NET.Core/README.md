# FormuLang.NET.Core

.NET implementation of the [**FormuLang**](https://github.com/Adisoreq/FormuLang) formula language, **Core** standard `core-1.2`.

A host application embeds the interpreter and lets its users write text formulas:

```php
$principal := 1000
$rate := 0.05
FORMAT("After 10 years: {:.2}", [$principal * (1 + $rate) ^ 10])
```

The library runs the execution pipeline of the standard (validation → metadata reading → clean-up → tokenization → parsing → evaluation) and returns a single result value. Every stage can be replaced with your own implementation of its interface from [FormuLang.NET.Abstractions](https://www.nuget.org/packages/FormuLang.NET.Abstractions).

## Usage

```csharp
using FormuLang.NET.Core.Processing;
using Environment = FormuLang.NET.Core.Structures.Environment;

var environment = Environment.Create();   // Core functions, constants and variables
var executor = Executor.Create();

ExecutionResult result = executor.Execute("SUM(MAP([1, 2, 3], (x) => x ^ 2))", environment);

Console.WriteLine(result.Successful ? result.ToString() : result.Error.ToString());   // 14
```

## Features

- Every function, procedure, error and operator of `core-1.2`, including delegate calls (`$f(x)`), the memory of the Iteration and Time Contexts (`STORE`, `RECALL`, `CLEAR`, `HISTORY`) and the short circuit of `AND` / `OR`: arithmetic, logic, text (Unicode code points), arrays, lambdas, objects, trigonometry, `FORMAT` and the time functions (`DATE`, `FORMAT_TIME`, `YEAR` ... `MILLISECOND`).
- Line comments `#` and scoped comments `<# ... #>`.
- Context types: **Default Context** (`Context`), **Iteration Context** (`IterationContext`, `Executor.ExecuteIterations`) and **Time Context** (`TimeContext`, `Executor.CreateFrameRunner` for animations and simulations).
- `NOW` read once per execution from a `TimeProvider` (or pinned with `Environment.SetNow`), `@time_offset` for local time.
- Validation that reports every problem at once, with line and column.
- Metadata blocks `<@ { ... } @>`, warnings of the preparation (`UNUSED_VALUE`, `CONTINUED_LINE`, `METADATA_TYPE`, `METADATA_CONTEXT`), prepared formulas for hot executions (`Executor.Prepare`, `Evaluate`) and resumed iterations (`ResumeIterations`, `IterationContext.SaveState`).
- Parameter specifications read from the metadata and their normalization (`FormuLang.NET.Core.Model`).

## Links

- [Documentation](https://github.com/Adisoreq/formulang-dotnet/blob/main/docs/README.md) · [Getting started](https://github.com/Adisoreq/formulang-dotnet/blob/main/docs/getting-started.md) · [Conformance](https://github.com/Adisoreq/formulang-dotnet/blob/main/docs/conformance.md) · [Changelog](https://github.com/Adisoreq/formulang-dotnet/blob/main/CHANGELOG.md)
- [FormuLang standard](https://github.com/Adisoreq/FormuLang)
- [Web preview](https://adisoreq.github.io/formulang-dotnet/)
