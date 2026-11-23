# WebPreview

Sample of [FormuLang.NET](../../README.md): an editor that executes formulas of the Core and Stock standards in the browser, in every context type. It is a [Blazor WebAssembly](https://learn.microsoft.com/aspnet/core/blazor/) application that references the projects of the library, so the formulas run in .NET inside the browser. The chart is drawn by [lightweight-charts](https://github.com/tradingview/lightweight-charts) through JavaScript interop.

It is the counterpart of the [web preview of FormuLang-TS](https://github.com/Adisoreq/formulang-ts/tree/main/samples/web-preview): the same examples, the same mock candles (a seed gives the same chart in both previews) and the same layout, so the results of the two implementations can be compared side by side.

## Running

```bash
dotnet run --project samples/WebPreview
```

The application opens at `http://localhost:5174`. A change of the library needs only a new `dotnet run`.

```bash
dotnet publish samples/WebPreview -c Release
```

`dotnet publish` builds a static site in `bin/Release/net8.0/publish/wwwroot`. Serve it with any HTTP server; opening `index.html` from the disk (`file://`) does not work. To host it in a subdirectory (e.g. GitHub Pages), change `<base href="/" />` in `wwwroot/index.html`.

### Performance

Without ahead-of-time compilation the .NET runtime in the browser interprets the code of the library, roughly ten times slower than a native .NET application: most examples execute in milliseconds, the strategies in a few seconds and MACD (three nested `LOAD_INDICATOR` calls for every candle) in about ten. The execution runs on the UI thread, so the page does not respond meanwhile. [AOT compilation](https://learn.microsoft.com/aspnet/core/blazor/webassembly-build-tools-and-aot) makes a published site considerably faster, at the cost of a larger download; it needs the `wasm-tools` workload and `<RunAOTCompilation>true</RunAOTCompilation>` in the project.

## Features

- **Context types**: the panel on the left has an expandable list for every standard (Core 1.0, Stock 1.0); its items are the context types. Every context type has its own examples, and a _New formula_ to write from scratch (for the Stock contexts with a default configuration: the empty indicator is `LOAD_INDICATOR("CUSTOM")`, the empty pattern `LOAD_PATTERN("CUSTOM")`, the empty strategy is inactive until it is turned on).
- **Editor**: syntax highlighting, and a list of the keywords that contain the word being typed – functions (with their overloads), procedures, constants of the environment and of the context type, environment variables, the variables of the formula and the literals. <kbd>↑</kbd> <kbd>↓</kbd> choose, <kbd>Enter</kbd> or <kbd>Tab</kbd> insert, <kbd>Esc</kbd> closes.
- **Default Context**: the result in a console, the parsed and translated sequences, and the variables of the execution (changed environment variables are highlighted).
- **Iteration Context**: a slider over the iterations (`I`, `N`), the result of the chosen iteration and the list of all of them.
- **Time Context**: the frames are played as time passes (_Play_ / _Pause_, _Step_, _Reset_, frames per second). The output shows the constants of the last frame (`FRAME`, `TIME_S`, `DTIME_MS`), its result, a plot of the Numbers returned by the last 240 frames and the list of the last frames. `DTIME_S` is the measured time between two frames, and `NOW` is read again for every frame. The frames stop at the first Error.
- **Default Context examples** of Core 1.0: `FORMAT`, the time functions (`NOW`, `DATE`, `FORMAT_TIME`, `@time_offset`), scoped comments `<# … #>` and Unicode texts.
- **Stock contexts**: a candlestick chart of mock candles; _Generate candles_ creates new ones (with a new seed) and reloads the chart.
  - **Stock Context**: the slider marks the current candle `I` on the chart.
  - **Pattern Context**: a rectangle around every detected pattern. A pattern has a name and a description, a registry id (`LOAD_PATTERN("ENGULFING")`), parameters (id, name, default value), a type (bullish, bearish) and a number of candles, edited below the formula; its color (green, red), label and the parameter values of the chart are set below the chart.
  - **Indicator Context**: the series of the indicator on the chart. An indicator has a name and a description, a registry id (`LOAD_INDICATOR("SMA")`), parameters (id, name, default value), series (id, name and description, line color, fill color, fill mode: line, area, baseline or histogram), a series mode (single: a Number per candle, or multiple: an Object with a Number per series) and a type (normal: over the candles, on their price scale; oscillator: in a separate pane below the candles, with its own price scale – e.g. MACD, RSI). Below the formula you edit the definition; below the chart, the style of every series and the parameter values of the chart. The registry uses the default values, so other formulas get `LOAD_INDICATOR(id)` with them. An indicator with an invalid definition shows its problems and is left out of the registry.
  - **Strategy Context**: an arrow up (buy) or down (sell) for every signal of the active strategies. A strategy has a name and a description, a confidence (default 1.0) and a lifetime (whole number of candles), edited below the formula as the defaults of its definition. Below the chart every strategy can be turned on and off, and gets its marker label and the confidence and lifetime used by the proposal. The value of a strategy is its result (from -1 to 1) × its confidence, active for its lifetime; the final proposal is the sum of the values of all active strategies, shown below the chart and for the last candle. Changing these values does not execute the formulas again.

Every indicator and pattern of the preview is in the formula registry of the environment with its current formula and the default values of its parameters, so the strategies (and other formulas) can use them with `LOAD_INDICATOR("SMA")` and `LOAD_PATTERN("ENGULFING")`.

## Files

| File | Content |
| :--- | :------ |
| [Model/Examples.cs](Model/Examples.cs) | Standards, context types and their examples: the configurations of the patterns, indicators and strategies. |
| [Model/Engine.cs](Model/Engine.cs) | Execution of the formulas with FormuLang.NET in every context type. |
| [Model/Language.cs](Model/Language.cs) | Syntax highlighting and suggestions of the editor. |
| [Model/Candles.cs](Model/Candles.cs) | Mock candles: a seeded random walk with trend regimes. |
| [Model/PreviewState.cs](Model/PreviewState.cs) | State of the preview; [Model/Drafts.cs](Model/Drafts.cs): the editable indicators, patterns and strategies. |
| [App.razor](App.razor) | Layout, and the execution of the formula 200 ms after the last change. |
| [Components](Components) | Sidebar, editor, configuration panels, outputs and the chart. |
| [wwwroot/js](wwwroot/js) | Browser side of the chart (lightweight-charts, pattern rectangles) and of the editor (caret, keys). |
| [wwwroot/lib/lightweight-charts](wwwroot/lib/lightweight-charts) | lightweight-charts 5.3.0 (Apache License 2.0): its standalone ES module build, renamed to `.js` so that every server sends it as JavaScript. |
