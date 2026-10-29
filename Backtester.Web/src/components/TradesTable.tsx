import { For, Show } from 'solid-js';
import { num, type ClosedTrade, type OpenTrade } from '../lib/api';
import { dateTime, money, percent, sign } from '../lib/format';

export default function TradesTable(props: { trades: ClosedTrade[]; open: OpenTrade[] }) {
  return (
    <div class="panel overflow-auto max-h-[480px]">
      <table class="w-full text-sm tabular-nums whitespace-nowrap">
        <thead class="sticky top-0 bg-panel text-muted text-xs uppercase">
          <tr class="text-right [&>th]:px-3 [&>th]:py-2 [&>th]:font-medium">
            <th class="text-left">#</th>
            <th class="text-left">Tipo</th>
            <th class="text-left">Segnali</th>
            <th class="text-left">Entrata</th>
            <th class="text-left">Uscita</th>
            <th>Prezzi</th>
            <th>Qtà</th>
            <th>Profitto</th>
            <th>Run-up</th>
            <th>Drawdown</th>
          </tr>
        </thead>
        <tbody>
          <Show when={props.open.length}>
            <For each={props.open}>{t => (
              <tr class="text-right border-t border-line bg-yellow-500/5 [&>td]:px-3 [&>td]:py-1.5">
                <td class="text-left text-muted">—</td>
                <td class="text-left">{t.direction} <span class="text-yellow-400">aperto</span></td>
                <td class="text-left">{t.entryId}</td>
                <td class="text-left">{dateTime(t.entryTime)}</td>
                <td class="text-left text-muted">—</td>
                <td>{money(t.entryPrice)}</td>
                <td>{money(t.quantity, 4)}</td>
                <td colspan="3" />
              </tr>
            )}</For>
          </Show>
          <For each={[...props.trades].reverse()}>{t => (
            <tr class="text-right border-t border-line hover:bg-white/3 [&>td]:px-3 [&>td]:py-1.5">
              <td class="text-left text-muted">{t.number}</td>
              <td class={`text-left ${t.direction === 'Long' ? 'text-up' : 'text-down'}`}>{t.direction}</td>
              <td class="text-left">{t.entryId} → {t.exitId}</td>
              <td class="text-left">{dateTime(t.entryTime)}</td>
              <td class="text-left">{dateTime(t.exitTime)}</td>
              <td>{money(t.entryPrice)} → {money(t.exitPrice)}</td>
              <td>{money(t.quantity, 4)}</td>
              <td class={sign(t.profit)}>
                {money(t.profit)} <span class="text-xs opacity-70">{percent(t.profitPercent)}</span>
              </td>
              <td class="text-up/80">{money(t.runUp)}</td>
              <td class="text-down/80">{money(num(t.drawdown))}</td>
            </tr>
          )}</For>
        </tbody>
      </table>
      <Show when={!props.trades.length && !props.open.length}>
        <p class="p-4 text-muted text-sm">Nessun trade.</p>
      </Show>
    </div>
  );
}
