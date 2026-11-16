// Candlestick chart (lightweight-charts) with indicator series, pattern rectangles and strategy markers.
// Components/StockChart.razor creates a StockChart for its element and passes the data as JSON.
import {
  AreaSeries,
  BaselineSeries,
  CandlestickSeries,
  ColorType,
  CrosshairMode,
  HistogramSeries,
  LineSeries,
  createChart,
  createSeriesMarkers,
} from "../lib/lightweight-charts/lightweight-charts.esm.js";

const css = (name) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
const timeOf = (candle) => Math.floor(candle.time / 1000);

/** `#rrggbb` with an alpha channel. */
function withAlpha(color, alpha) {
  const match = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(color);
  if (match === null) return color;
  const [r, g, b] = match.slice(1).map((h) => Number.parseInt(h, 16));
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

// Pattern rectangles: a series primitive drawn below the candles

class RectanglesRenderer {
  constructor(source) {
    this.source = source;
  }

  draw(target) {
    const { chart, series } = this.source;
    if (chart === undefined || series === undefined) return;
    const timeScale = chart.timeScale();
    const half = timeScale.options().barSpacing / 2 + 2;
    target.useMediaCoordinateSpace(({ context }) => {
      context.font = "600 10px ui-monospace, 'Cascadia Code', Consolas, monospace";
      for (const rect of this.source.rectangles) {
        const x1 = timeScale.timeToCoordinate(rect.from);
        const x2 = timeScale.timeToCoordinate(rect.to);
        const y1 = series.priceToCoordinate(rect.top);
        const y2 = series.priceToCoordinate(rect.bottom);
        if (x1 === null || x2 === null || y1 === null || y2 === null) continue;
        const left = x1 - half;
        const top = y1 - 4;
        const width = x2 - x1 + 2 * half;
        const height = y2 - y1 + 8;
        context.fillStyle = withAlpha(rect.color, 0.14);
        context.fillRect(left, top, width, height);
        context.strokeStyle = withAlpha(rect.color, 0.9);
        context.lineWidth = 1;
        context.setLineDash([4, 3]);
        context.strokeRect(left + 0.5, top + 0.5, width, height);
        context.setLineDash([]);
        context.fillStyle = withAlpha(rect.color, 1);
        context.fillText(rect.label, left, top - 4);
      }
    });
  }
}

class RectanglesPrimitive {
  rectangles = [];
  #requestUpdate;
  #views = [{ zOrder: () => "bottom", renderer: () => new RectanglesRenderer(this) }];

  attached({ chart, series, requestUpdate }) {
    this.chart = chart;
    this.series = series;
    this.#requestUpdate = requestUpdate;
  }

  detached() {
    this.chart = undefined;
    this.series = undefined;
    this.#requestUpdate = undefined;
  }

  paneViews() {
    return this.#views;
  }

  setRectangles(rectangles) {
    this.rectangles = rectangles;
    this.#requestUpdate?.();
  }
}

class StockChart {
  constructor(container) {
    this.container = container;
    this.candles = [];
    this.overlays = [];
  }

  /** New candles rebuild the whole chart. */
  setCandles(json) {
    this.candles = JSON.parse(json);
    this.chart?.remove();
    this.overlays = [];
    const up = css("--up");
    const down = css("--down");
    this.chart = createChart(this.container, {
      autoSize: true,
      layout: {
        background: { type: ColorType.Solid, color: "transparent" },
        textColor: css("--muted"),
        fontFamily: css("--font-mono"),
        fontSize: 11,
        attributionLogo: false,
        panes: { separatorColor: css("--line"), enableResize: false },
      },
      grid: { vertLines: { color: css("--grid") }, horzLines: { color: css("--grid") } },
      crosshair: { mode: CrosshairMode.Normal },
      rightPriceScale: { borderColor: css("--line") },
      timeScale: { borderColor: css("--line"), rightOffset: 4 },
    });
    this.candleSeries = this.chart.addSeries(CandlestickSeries, {
      upColor: up,
      downColor: down,
      borderVisible: false,
      wickUpColor: up,
      wickDownColor: down,
    });
    this.candleSeries.setData(
      this.candles.map((c) => ({ time: timeOf(c), open: c.open, high: c.high, low: c.low, close: c.close })),
    );
    this.markers = createSeriesMarkers(this.candleSeries, []);
    this.rectangles = new RectanglesPrimitive();
    this.candleSeries.attachPrimitive(this.rectangles);
    this.chart.timeScale().fitContent();
  }

  /** Series of the indicator and the histogram of the proposal: `{ overlays, histogram }`. */
  setOverlays(json) {
    const { chart } = this;
    if (chart === undefined) return;
    const { overlays = [], histogram = null } = JSON.parse(json);
    for (const series of this.overlays) chart.removeSeries(series);
    this.overlays = [];
    const times = this.candles.map(timeOf);
    const data = (values) => values.map((value, i) => (value === null ? { time: times[i] } : { time: times[i], value }));

    for (const { config, values, pane = 0 } of overlays) {
      const common = { title: config.name, lineWidth: 2, priceLineVisible: false, lastValueVisible: true };
      let series;
      switch (config.fillMode) {
        case "area":
          series = chart.addSeries(
            AreaSeries,
            { ...common, lineColor: config.lineColor, topColor: config.fillColor, bottomColor: "rgba(0, 0, 0, 0)" },
            pane,
          );
          break;
        case "baseline": {
          const numbers = values.filter((v) => v !== null);
          const base = numbers.length > 0 ? numbers.reduce((a, b) => a + b, 0) / numbers.length : 0;
          series = chart.addSeries(
            BaselineSeries,
            {
              ...common,
              baseValue: { type: "price", price: base },
              topLineColor: config.lineColor,
              topFillColor1: withAlpha(config.lineColor, 0.25),
              topFillColor2: withAlpha(config.lineColor, 0.02),
              bottomLineColor: css("--down"),
              bottomFillColor1: config.fillColor,
              bottomFillColor2: withAlpha(css("--down"), 0.02),
            },
            pane,
          );
          break;
        }
        case "histogram":
          if (pane === 0) {
            // Over the candles: on its own scale at the bottom of the pane, like a volume histogram
            series = chart.addSeries(HistogramSeries, {
              title: config.name,
              color: config.fillColor,
              priceScaleId: `overlay-${config.id}`,
              priceLineVisible: false,
              lastValueVisible: false,
            });
            series.priceScale().applyOptions({ scaleMargins: { top: 0.78, bottom: 0 } });
          } else {
            // In the pane of an oscillator: on the price scale of the pane
            series = chart.addSeries(
              HistogramSeries,
              { title: config.name, color: config.fillColor, priceLineVisible: false, lastValueVisible: true },
              pane,
            );
          }
          break;
        default:
          series = chart.addSeries(LineSeries, { ...common, color: config.lineColor }, pane);
      }
      series.setData(data(values));
      this.overlays.push(series);
    }
    // Panes below the candles (oscillators): a third of the chart
    const panes = chart.panes();
    for (let i = 1; i < panes.length; i++) panes[i]?.setHeight(Math.round(this.container.clientHeight * 0.32));

    if (histogram !== null) {
      const series = chart.addSeries(
        HistogramSeries,
        { title: histogram.label, priceLineVisible: false, lastValueVisible: true },
        1,
      );
      series.setData(
        histogram.values.map((value, i) => ({
          time: times[i],
          value,
          color: value > 0 ? css("--up") : value < 0 ? css("--down") : css("--muted"),
        })),
      );
      chart.panes()[1]?.setHeight(110);
      this.overlays.push(series);
    }
  }

  /** Strategy markers and pattern rectangles: `{ markers, patterns }`. */
  setDecorations(json) {
    const { markers = [], patterns = [] } = JSON.parse(json);
    const times = this.candles.map(timeOf);
    const list = markers
      .filter((m) => times[m.index] !== undefined)
      .map((m) => ({ time: times[m.index], position: m.position, shape: m.shape, color: m.color, text: m.text }))
      .sort((a, b) => a.time - b.time);
    this.markers?.setMarkers(list);
    const boxes = patterns.map((p) => {
      const slice = this.candles.slice(p.from, p.to + 1);
      return {
        from: times[p.from],
        to: times[p.to],
        top: Math.max(...slice.map((c) => c.high)),
        bottom: Math.min(...slice.map((c) => c.low)),
        color: p.color,
        label: p.label,
      };
    });
    this.rectangles?.setRectangles(boxes);
  }

  dispose() {
    this.chart?.remove();
    this.chart = undefined;
  }
}

export function create(container) {
  return new StockChart(container);
}
