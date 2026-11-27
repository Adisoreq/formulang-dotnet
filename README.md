<div align="center">

<img src="./raw/images/icons/logotype.svg" alt="FormuLang logo" width="500" />

**FormuLang.NET** is a multi-platform, modular C# library implementing the [**FormuLang**](https://github.com/Adisoreq/FormuLang) standard.

![GitHub License](https://img.shields.io/github/license/Adisoreq/formulang-dotnet)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
[![NuGet](https://img.shields.io/nuget/v/FormuLang.NET.Core?label=NuGet)](https://www.nuget.org/packages/FormuLang.NET.Core)
[![Standards](https://img.shields.io/badge/standards-core--1.2%20%C2%B7%20stock--1.2-2fbf71)](docs/conformance.md)

[**Documentation**](docs/README.md) · [**Getting started**](docs/getting-started.md) · [**Conformance**](docs/conformance.md) · [**Changelog**](CHANGELOG.md)

---

**Web preview** available here:

<a href="https://adisoreq.github.io/formulang-dotnet/">
  <img src="https://img.shields.io/badge/%E2%96%B6-Run%20Web%20Preview-512BD4?style=for-the-badge&labelColor=2E1A87" alt="Run Web Preview button" />
</a>

</div>


## Overview

[**FormuLang**](https://github.com/Adisoreq/FormuLang) is a formula-based scripting language standard: a host application embeds an interpreter and lets its users write text formulas such as

```php
$principal := 1000
$rate := 0.05
$principal * (1 + $rate) ^ 10
```

**FormuLang.NET** is the reference implementation of that standard for .NET. It turns a formula into a single result value by running the execution pipeline defined by the standard:

**Validation** → **Clean-up** → **Tokenization** → **Parsing** → **Evaluation**

Each stage is defined by an interface in `FormuLang.NET.Abstractions` and implemented in `FormuLang.NET.Core`, so a host application can replace any single stage with its own implementation.


## Library modules

Module list:

| Module | Description |
| :----- | :---------- |
| [**FormuLang.NET.Abstractions**](src/FormuLang.NET.Abstractions) | Interfaces and enumerations shared by every module: primitives (`IValue`, `IType`, `IContainer`, `IFunction`), pipeline stages (`IValidator`, `ICleaner`, `ITokenizer`, `IParser`, `IEvaluator`), structures (`IEnvironment`, `IContext`, `IStandard`). No dependencies. |
| [**FormuLang.NET.Core**](src/FormuLang.NET.Core) | Implementation of the **Core** standard: the execution pipeline, primitives, errors, operators, built-in functions and procedures. |
| [**FormuLang.NET.Extensions.Stock**](src/FormuLang.NET.Extensions.Stock) | Implementation of the **Stock** extension standard: the `Stock.Candle` type, the Stock, Indicator, Pattern and Strategy contexts, the formula registry of indicators and patterns, `CANDLE`, `IS_BULLISH`, `IS_BEARISH`, `LOAD_INDICATOR` and `LOAD_PATTERN`. |

Dependency tree:

```mermaid
flowchart RL

A[FormuLang.NET.Abstractions]
C[FormuLang.NET.Core]
ES[FormuLang.NET.Extensions.Stock]

ES --> C --> A

```

More: [Architecture](docs/architecture.md).


## Installation

The modules are published on [NuGet](https://www.nuget.org/packages?q=FormuLang.NET) as separate packages:

| Package | Standard | When you need it |
| :------ | :------- | :--------------- |
| [FormuLang.NET.Core](https://www.nuget.org/packages/FormuLang.NET.Core) | `core-1.2` (and `core-1.0`, `core-1.1`) | Always: the language and its execution pipeline. Brings `FormuLang.NET.Abstractions` with it. |
| [FormuLang.NET.Extensions.Stock](https://www.nuget.org/packages/FormuLang.NET.Extensions.Stock) | `stock-1.2` (and `stock-1.0`, `stock-1.1`) | Formulas on market data: candles, indicators, patterns, strategies, and the formula models (definitions, instances, runs) for host applications. |
| [FormuLang.NET.Abstractions](https://www.nuget.org/packages/FormuLang.NET.Abstractions) | - | Only to implement your own pipeline stages without the Core module. |

### 1. Check the requirements

The library targets .NET 8.0, so the project needs .NET 8.0 or newer (e.g. `<TargetFramework>net8.0</TargetFramework>` or `net10.0`). Check the installed SDK:

```bash
dotnet --version
```

### 2. Add the package

In the directory of the project (the one with the `.csproj` file):

```bash
dotnet add package FormuLang.NET.Core
```

The command adds a reference to the `.csproj` file. You can also add it by hand, or with the NuGet package manager of Visual Studio or Rider:

```xml
<ItemGroup>
  <PackageReference Include="FormuLang.NET.Core" Version="1.2.*" />
</ItemGroup>
```

Within `1.x`, new versions are backward compatible, so `1.2.*` (or `1.*`) gets the fixes without breaking your code.

### 3. Run your first formula

Replace the content of `Program.cs`:

```csharp
using FormuLang.NET.Core.Processing;
using Environment = FormuLang.NET.Core.Structures.Environment;

var result = Executor.Create().Execute("$double = (x) => x * 2
$double(21)", Environment.Create());
Console.WriteLine(result.Successful ? result.Value : result.Error);   // 42
```

```bash
dotnet run
```

### 4. Add the Stock standard (optional)

```bash
dotnet add package FormuLang.NET.Extensions.Stock
```

```csharp
using FormuLang.NET.Extensions.Stock;
using FormuLang.NET.Extensions.Stock.Registry;

var environment = Environment.Create();
var registry = environment.RegisterStock().Registry;   // LOAD_INDICATOR, LOAD_CANDLES, CROSSOVER, SCORE, Stock.Candle, stock-1.2
registry.Indicators.Add("SMA", "WINDOW(SOURCE, PARAMETERS.period, AVG)", new FormulaParameter("period", 20));
```

The next steps (contexts, iterations over candles, your own functions) are in [Getting started](docs/getting-started.md).


## Quick example

```csharp
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Core.Structures;
using Environment = FormuLang.NET.Core.Structures.Environment;

var environment = Environment.Create();       // Core standard: functions, constants, variables
var context = Context.Create(environment);    // basic Core context, chosen by the host application

ExecutionResult result = Executor.Create().Execute("""
    $principal := 1000
    $rate := 0.05
    ROUND($principal * (1 + $rate) ^ 10, 2)
    """, context);

Console.WriteLine(result.Successful ? result.Value : result.Error);   // 1628.89
```

The host application chooses the context a formula runs in: the basic Core context, a persistent one, or a context type that provides its own data (e.g. the candles of a stock indicator). Errors are values: a formula that fails returns an **Error** such as `[OPERATION_ERROR (3)] Division by zero.` instead of throwing. See [Getting started](docs/getting-started.md) and the [CalculatorCLI sample](samples/CalculatorCLI).


## Building

Requirements: [.NET SDK 8.0](https://dotnet.microsoft.com/download) or newer.

```bash
dotnet build FormuLang.NET.sln
```

```bash
dotnet test FormuLang.NET.sln
```

The tests include the language-independent [conformance tests](tests/FormuLang.NET.Conformance.Tests) of the standard. Releases are described in [Development](docs/development.md#releasing).


## Related projects

- [**FormuLang**](https://github.com/Adisoreq/FormuLang)
<br> Language standard, reference point for syntax and semantics rules.

- [**FormuLang-TS**](https://github.com/Adisoreq/formulang-ts)
<br> TypeScript implementation of the standard.


## Authors

Adrian Piznal


## License

The library is licensed under the [MIT License](LICENSE).
The FormuLang standard documentation is licensed separately, under [CC BY 4.0](https://github.com/Adisoreq/FormuLang/blob/main/LICENSE.txt).

<div align="center">
<img src="raw/images/icons/monogram.svg" width="50" alt="Monogram" />
</div>
