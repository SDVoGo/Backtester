import { onCleanup, onMount, createEffect } from 'solid-js';
import {
  createChart, createSeriesMarkers, CandlestickSeries, LineSeries, HistogramSeries,
  type IChartApi, type ISeriesApi, type SeriesMarker, type Time, type UTCTimestamp,
} from 'lightweight-charts';
import { num, type BacktestResponse } from '../lib/api';

const PLOT_COLORS = ['#f5c542', '#5b8def', '#c678dd', '#56b6c2', '#e5c07b'];

const seconds = (ms: number) => (ms / 1000) as UTCTimestamp;

/**
 * One chart, three panes sharing the time axis: candles with the script's plots and the trades,
 * equity, drawdown.
 */
export default function StrategyChart(props: { result: BacktestResponse }) {
  let container!: HTMLDivElement;
  let chart: IChartApi | undefined;
  let series: ISeriesApi<any>[] = [];

  onMount(() => {
    chart = createChart(container, {
      autoSize: true,
      layout: { background: { color: '#171a21' }, textColor: '#8a93a6', panes: { separatorColor: '#262b36' } },
      grid: { vertLines: { color: '#1e222b' }, horzLines: { color: '#1e222b' } },
      timeScale: { timeVisible: true, borderColor: '#262b36' },
      rightPriceScale: { borderColor: '#262b36' },
      localization: { locale: 'it-IT' },
    });
    onCleanup(() => chart?.remove());
  });

  createEffect(() => {
    const result = props.result;
    if (!chart) return;
    for (const s of series) chart.removeSeries(s);
    series = [];

    const times = result.bars.map(b => seconds(b.time));

    const candles = chart.addSeries(CandlestickSeries, {
      upColor: '#26a69a', downColor: '#ef5350', wickUpColor: '#26a69a', wickDownColor: '#ef5350', borderVisible: false,
    });
    candles.setData(result.bars.map((b, i) => ({ time: times[i], open: b.open, high: b.high, low: b.low, close: b.close })));
    series.push(candles);

    result.plots.forEach((plot, p) => {
      const line = chart!.addSeries(LineSeries, {
        color: PLOT_COLORS[p % PLOT_COLORS.length], lineWidth: 1, title: plot.title,
        priceLineVisible: false, lastValueVisible: false,
      });
      line.setData(plot.values.flatMap((v, i) => {
        const value = num(v);
        return Number.isFinite(value) && i < times.length ? [{ time: times[i], value }] : [];
      }));
      series.push(line);
    });

    const markers: SeriesMarker<Time>[] = [];
    for (const t of result.trades) {
      const long = t.direction === 'Long';
      markers.push({
        time: seconds(t.entryTime), position: long ? 'belowBar' : 'aboveBar',
        shape: long ? 'arrowUp' : 'arrowDown', color: long ? '#26a69a' : '#ef5350', text: t.entryId,
      });
      markers.push({
        time: seconds(t.exitTime), position: long ? 'aboveBar' : 'belowBar',
        shape: 'circle', color: num(t.profit) >= 0 ? '#9be7c4' : '#f4a3a1', size: 0.6,
      });
    }
    for (const t of result.openTrades) {
      const long = t.direction === 'Long';
      markers.push({
        time: seconds(t.entryTime), position: long ? 'belowBar' : 'aboveBar',
        shape: long ? 'arrowUp' : 'arrowDown', color: '#f5c542', text: `${t.entryId} (aperto)`,
      });
    }
    markers.sort((a, b) => (a.time as number) - (b.time as number));
    createSeriesMarkers(candles, markers);

    const equity = chart.addSeries(LineSeries, { color: '#5b8def', lineWidth: 2, title: 'Equity', priceLineVisible: false }, 1);
    equity.setData(result.equity.map(e => ({ time: seconds(e.time), value: num(e.equity) })));
    series.push(equity);

    const drawdown = chart.addSeries(HistogramSeries, { color: 'rgba(239,83,80,0.6)', title: 'Drawdown', priceLineVisible: false }, 2);
    drawdown.setData(result.equity.map(e => ({ time: seconds(e.time), value: -num(e.drawdown) })));
    series.push(drawdown);

    const panes = chart.panes();
    panes[0]?.setStretchFactor(3);
    panes[1]?.setStretchFactor(1.2);
    panes[2]?.setStretchFactor(0.7);
    chart.timeScale().fitContent();
  });

  return <div ref={container} class="w-full h-[640px]" />;
}
