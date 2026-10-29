import { For } from 'solid-js';
import type { Metrics } from '../lib/api';
import { money, percent, sign } from '../lib/format';

type Kind = 'money' | 'percent' | 'number' | 'ratio';

const MAIN: [string, string, Kind][] = [
  ['Profitto netto', 'netProfit', 'money'],
  ['Profitto netto %', 'netProfitPercent', 'percent'],
  ['Trade chiusi', 'totalClosedTrades', 'number'],
  ['% vincenti', 'percentProfitable', 'percent'],
  ['Profit factor', 'profitFactor', 'ratio'],
  ['Max drawdown %', 'maxDrawdownPercent', 'percent'],
  ['Sharpe', 'sharpeRatio', 'ratio'],
  ['Buy & hold %', 'buyAndHoldReturnPercent', 'percent'],
];

const DETAILS: [string, string, Kind][] = [
  ['Capitale iniziale', 'initialCapital', 'money'],
  ['Equity finale', 'finalEquity', 'money'],
  ['Profitto lordo', 'grossProfit', 'money'],
  ['Perdita lorda', 'grossLoss', 'money'],
  ['Profitto aperto', 'openProfit', 'money'],
  ['Commissioni', 'commissionPaid', 'money'],
  ['Vincenti / perdenti', '', 'number'],
  ['Trade medio', 'averageTrade', 'money'],
  ['Media vincenti', 'averageWinningTrade', 'money'],
  ['Media perdenti', 'averageLosingTrade', 'money'],
  ['Win / loss medio', 'ratioAverageWinAverageLoss', 'ratio'],
  ['Miglior trade', 'largestWinningTrade', 'money'],
  ['Peggior trade', 'largestLosingTrade', 'money'],
  ['Barre medie in trade', 'averageBarsInTrade', 'ratio'],
  ['Max drawdown', 'maxDrawdown', 'money'],
  ['Max run-up', 'maxRunUp', 'money'],
  ['Max run-up %', 'maxRunUpPercent', 'percent'],
  ['Sortino', 'sortinoRatio', 'ratio'],
];

function show(metrics: Metrics, key: string, kind: Kind) {
  if (!key) return `${metrics.winningTrades} / ${metrics.losingTrades}`;
  const value = metrics[key];
  switch (kind) {
    case 'money': return money(value);
    case 'percent': return percent(value);
    case 'ratio': return money(value, 3);
    default: return String(value ?? '—');
  }
}

const colored = (key: string) => ['netProfit', 'netProfitPercent', 'openProfit', 'averageTrade', 'buyAndHoldReturnPercent'].includes(key);

export default function MetricsPanel(props: { metrics: Metrics }) {
  return (
    <div class="space-y-3">
      <div class="grid grid-cols-2 md:grid-cols-4 xl:grid-cols-8 gap-2">
        <For each={MAIN}>{([label, key, kind]) => (
          <div class="panel px-3 py-2">
            <div class="label">{label}</div>
            <div class={`text-lg font-semibold tabular-nums ${colored(key) ? sign(props.metrics[key]) : ''}`}>
              {show(props.metrics, key, kind)}
            </div>
          </div>
        )}</For>
      </div>
      <details class="panel px-3 py-2">
        <summary class="cursor-pointer text-sm text-muted">Tutte le metriche</summary>
        <div class="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-6 gap-x-6 gap-y-1 mt-2 text-sm">
          <For each={DETAILS}>{([label, key, kind]) => (
            <div class="flex justify-between gap-2 border-b border-line py-1">
              <span class="text-muted">{label}</span>
              <span class={`tabular-nums ${colored(key) ? sign(props.metrics[key]) : ''}`}>{show(props.metrics, key, kind)}</span>
            </div>
          )}</For>
        </div>
      </details>
    </div>
  );
}
