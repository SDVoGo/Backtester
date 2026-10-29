import { createSignal, For, onMount, Show } from 'solid-js';
import { loadSample, runBacktest, type BacktestError, type BacktestResponse } from '../lib/api';
import { dateTime } from '../lib/format';
import StrategyChart from './StrategyChart';
import MetricsPanel from './MetricsPanel';
import TradesTable from './TradesTable';

const SAMPLES = ['strategy001', 'test002'];

export default function App() {
  const [script, setScript] = createSignal('');
  const [inputs, setInputs] = createSignal('');
  const [minTick, setMinTick] = createSignal(0.01);
  const [data, setData] = createSignal<File | null>(null);
  const [result, setResult] = createSignal<BacktestResponse>();
  const [error, setError] = createSignal<BacktestError>();
  const [running, setRunning] = createSignal(false);
  const [elapsed, setElapsed] = createSignal(0);

  async function pick(name: string) {
    try {
      setScript(await loadSample(name));
      setInputs('');
    } catch (e) {
      setError(e as BacktestError);
    }
  }

  async function run() {
    setRunning(true);
    setError(undefined);
    const start = performance.now();
    try {
      setResult(await runBacktest({ script: script(), data: data(), inputs: inputs(), minTick: minTick() }));
      setElapsed(performance.now() - start);
    } catch (e) {
      setError(e instanceof TypeError ? { message: `Backend non raggiungibile: ${e.message}` } : e as BacktestError);
    } finally {
      setRunning(false);
    }
  }

  onMount(async () => {
    await pick(SAMPLES[0]);
    run();
  });

  function onEditorKey(e: KeyboardEvent) {
    const area = e.currentTarget as HTMLTextAreaElement;
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      run();
    } else if (e.key === 'Tab') {
      e.preventDefault();
      area.setRangeText('    ', area.selectionStart, area.selectionEnd, 'end');
      setScript(area.value);
    }
  }

  const range = () => {
    const bars = result()?.bars;
    return bars?.length ? `${dateTime(bars[0].time)} → ${dateTime(bars[bars.length - 1].time)} · ${bars.length} barre` : '';
  };

  return (
    <div class="flex flex-col lg:flex-row min-h-screen">
      <aside class="lg:w-[440px] shrink-0 border-b lg:border-b-0 lg:border-r border-line p-4 space-y-4 lg:h-screen lg:sticky lg:top-0 overflow-auto">
        <header class="flex items-baseline justify-between">
          <h1 class="text-lg font-semibold">Backtester <span class="text-muted font-normal">PineScript</span></h1>
          <a href="http://localhost:5014/docs" target="_blank" class="text-xs text-accent hover:underline">documentazione</a>
        </header>

        <div class="space-y-1">
          <div class="flex items-center justify-between">
            <span class="label">Strategia</span>
            <div class="flex gap-1">
              <For each={SAMPLES}>{name => (
                <button class="text-xs px-2 py-0.5 rounded border border-line hover:border-accent cursor-pointer" onClick={() => pick(name)}>
                  {name}
                </button>
              )}</For>
            </div>
          </div>
          <textarea
            class="field w-full h-[340px] font-mono text-xs leading-5 resize-y"
            spellcheck={false}
            wrap="off"
            value={script()}
            onInput={e => setScript(e.currentTarget.value)}
            onKeyDown={onEditorKey}
          />
        </div>

        <label class="block space-y-1">
          <span class="label">Input (JSON, opzionale)</span>
          <textarea
            class="field w-full h-16 font-mono text-xs"
            placeholder={'{"Fast SMA": 20}'}
            value={inputs()}
            onInput={e => setInputs(e.currentTarget.value)}
          />
        </label>

        <div class="grid grid-cols-[1fr_auto] gap-3">
          <label class="block space-y-1 min-w-0">
            <span class="label">Dati OHLCV (CSV)</span>
            <input type="file" accept=".csv" class="block w-full text-xs text-muted file:mr-2 file:btn file:border-0"
              onChange={e => setData(e.currentTarget.files?.[0] ?? null)} />
            <span class="block text-xs text-muted">{data() ? data()!.name : 'Default: BINANCE:BTCUSDT 1h (2024)'}</span>
          </label>
          <label class="block space-y-1">
            <span class="label">Min tick</span>
            <input type="number" step="any" class="field w-24" value={minTick()}
              onInput={e => setMinTick(Number(e.currentTarget.value))} />
          </label>
        </div>

        <button class="btn w-full py-2" disabled={running()} onClick={run}>
          {running() ? 'Esecuzione…' : 'Esegui backtest'} <span class="opacity-60 text-xs ml-1">Ctrl+Enter</span>
        </button>

        <Show when={error()}>{err => (
          <div class="rounded border border-down/50 bg-down/10 p-3 text-sm">
            <div class="font-medium text-down">
              Errore{err().stage ? ` (${err().stage})` : ''}
              {err().line ? ` · riga ${err().line}${err().column ? `:${err().column}` : ''}` : ''}
              {err().bar != null ? ` · barra ${err().bar}` : ''}
            </div>
            <div class="mt-1 text-gray-300">{err().message}</div>
          </div>
        )}</Show>
      </aside>

      <main class="flex-1 min-w-0 p-4 space-y-4">
        <Show when={result()} fallback={<p class="text-muted">{running() ? 'Esecuzione in corso…' : 'Esegui un backtest per vedere i risultati.'}</p>}>
          {r => (
            <>
              <div class="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted">
                <span class="text-gray-200 font-medium text-sm">{String(r().settings.title ?? '')}</span>
                <span>{range()}</span>
                <span>{Math.round(elapsed())} ms</span>
                <For each={Object.entries(r().inputs)}>{([k, v]) => (
                  <span class="px-1.5 py-0.5 rounded bg-line text-gray-300">{k}: {v}</span>
                )}</For>
              </div>
              <MetricsPanel metrics={r().metrics} />
              <div class="panel overflow-hidden">
                <StrategyChart result={r()} />
              </div>
              <h2 class="label">Trade ({r().trades.length} chiusi{r().openTrades.length ? `, ${r().openTrades.length} aperti` : ''})</h2>
              <TradesTable trades={r().trades} open={r().openTrades} />
            </>
          )}
        </Show>
      </main>
    </div>
  );
}
