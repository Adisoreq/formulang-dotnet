# FormuLang.NET.Extensions.Stock

**Stock** standard (`stock-1.1`) of [FormuLang.NET](https://github.com/Adisoreq/formulang-dotnet): market data in [**FormuLang**](https://github.com/Adisoreq/FormuLang) formulas. Indicators, candle patterns and trading strategies are formulas executed once for every candle of a chart.

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

## Features

- The `Stock.Candle` type (`Candle`) and the functions `CANDLE`, `IS_BULLISH`, `IS_BEARISH`, `LOAD_INDICATOR` and `LOAD_PATTERN`.
- Context types derived from the Core Iteration Context: `StockContext`, `IndicatorContext`, `PatternContext` and `StrategyContext`, with the checks of their expected results in `StockExecutor`.
- The **formula registry** (`FormulaRegistry`) with indicators and patterns; loaded series are computed once and reused.

[Stock standard](https://github.com/Adisoreq/FormuLang/blob/main/docs/standards/stock/README.md) · [Conformance](https://github.com/Adisoreq/formulang-dotnet/blob/main/docs/conformance.md) · [Changelog](https://github.com/Adisoreq/formulang-dotnet/blob/main/CHANGELOG.md)
