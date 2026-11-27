# FormuLang.NET.Extensions.Stock

**Stock** standard (`stock-1.2`) of [FormuLang.NET](https://github.com/Adisoreq/formulang-dotnet): market data in [**FormuLang**](https://github.com/Adisoreq/FormuLang) formulas. Indicators, candle patterns and trading strategies are formulas executed once for every candle of a chart.

## Usage

```csharp
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Extensions.Stock;
using FormuLang.NET.Extensions.Stock.Contexts;
using FormuLang.NET.Extensions.Stock.Registry;
using Environment = FormuLang.NET.Core.Structures.Environment;

var environment = Environment.Create();
FormulaRegistry registry = environment.RegisterStock().Registry;

// Indicators and patterns provided by the host application
registry.Indicators.Add("SMA", """
    $window = RANGE(SOURCE, MAX(I - PARAMETERS.period + 1, @array_indexing), I)
    IF(LEN($window) == PARAMETERS.period, AVG($window), null)
    """, new FormulaParameter("period", 14));

registry.Patterns.Add("UP_DAY", "IS_BULLISH(CANDLES[I])");

Candle[] candles = [new(10, 12.5, 9.5, 12), new(12, 12.5, 10.8, 11), new(10.5, 13.2, 10.4, 13)];

// A strategy that buys on a bullish candle above its 2-candle average
var context = StrategyContext.Create(environment, candles);
StrategyResult result = Executor.Create().ExecuteStrategy("""
    $sma = LOAD_INDICATOR("SMA", { period: 2 })[I]
    IF(AND(LOAD_PATTERN("UP_DAY")[I], NOT(ISNULL($sma))), IF(CLOSE[I] > $sma, BUY, NEUTRAL), NEUTRAL)
    """, context);

Console.WriteLine(string.Join(", ", result.Signals));   // 0, 0, 1
```

## Formula models

The [formula models](https://github.com/Adisoreq/FormuLang/blob/main/docs/implementation/formula_models.md) (`FormuLang.NET.Extensions.Stock.Models`) read indicators, patterns and strategies from the metadata of their formulas, so a host application does not repeat their code: parameter specifications with one normalization, instances, runs over a chart and live runs that continue when new candles close.

```csharp
using FormuLang.NET.Extensions.Stock.Models;

const string rsiFormula = """
    <@ {
      id: "RSI", context: "Stock.Indicator",
      parameters: { period: { type: "Number", default: 14, min: 1 } },
      stock: { category: "Momentum", outputs: { value: { name: "RSI" } } }
    } @>
    ... the formula of the RSI ...
    """;

var rsi = (IndicatorDefinition)Definition.FromFormula(rsiFormula);   // InvalidDefinitionException: ERROR, METADATA_TYPE, ID, CONTEXT
registry.AddDefinition(rsi);                                          // LOAD_INDICATOR("RSI") in other formulas

// Parameters given by a user (.NET values or a JsonElement): defaults, types, min / max, options
ParameterNormalization check = rsi.Normalize(new Dictionary<string, object?> { ["period"] = 0 });   // MIN problem
IndicatorInstance instance = rsi.CreateInstance(new Dictionary<string, object?> { ["period"] = 7 });
Console.WriteLine(instance.Key);                                      // RSI|{ period: 7 }

IndicatorSeries series = instance.Compute(environment, new Chart(candles) { Timeframe = "1d" });
double? last = series["value"][^1];

// A live run: only the new candles are executed (PREV, HISTORY and the memory are kept)
LiveIndicatorRun live = instance.Live(environment, new Chart(candles));
live.Start();
IndicatorSeries added = live.Append(newCandles);
IterationState state = live.SaveState();                              // RestoreState in a new live run later
```

A `StrategyInstance` gives the `StrategySignals`: every signal that is not `NEUTRAL`, with its `Reason`, `Checks` and the other keys of the signal object (`Extra`, e.g. `stop`). An instance is serialized with `ToJson()` and read back with `Definition.InstanceFromJson`, which normalizes its parameters again.

## Features

- The `Stock.Candle` type (`Candle`) and the functions `CANDLE`, `IS_BULLISH`, `IS_BEARISH`, `LOAD_INDICATOR`, `LOAD_PATTERN`, `LOAD_CANDLES`, `LAG`, `WINDOW`, the series functions (`READY`, `CROSSOVER`, `CROSSUNDER`, `HIGHEST`, `LOWEST`, `RISING`, `FALLING`, `BARS_SINCE`, `VALUE_WHEN`) and `SCORE`.
- Context types derived from the Core Iteration Context: `StockContext` (with `TIMEFRAME`, `INSTRUMENT` and other timeframes, `StockContextOptions`), `IndicatorContext`, `PatternContext` and `StrategyContext` (with `PARAMETERS` and signal objects), with the checks of their expected results in `StockExecutor`.
- The **formula registry** (`FormulaRegistry`) with indicators and patterns, also read from the metadata of their formulas (`AddFormula`, `AddDefinition`); loaded series are computed once and reused.
- The **formula models** (`Models`): definitions, instances, runs and live runs.

[Stock standard](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/stock/README.md) · [Conformance](https://github.com/Adisoreq/formulang-dotnet/blob/main/docs/conformance.md) · [Changelog](https://github.com/Adisoreq/formulang-dotnet/blob/main/CHANGELOG.md)
