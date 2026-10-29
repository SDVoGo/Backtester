// Shapes of POST /api/backtest (see BacktestController.BacktestResponse).
// Numbers can arrive as "NaN" / "Infinity" strings: read them through num().

export type Num = number | string;
export type Direction = 'Long' | 'Short';

export interface Bar { time: number; open: number; high: number; low: number; close: number; volume: number }

export interface ClosedTrade {
  number: number;
  entryId: string;
  exitId: string;
  direction: Direction;
  quantity: Num;
  entryBar: number;
  entryTime: number;
  entryPrice: Num;
  exitBar: number;
  exitTime: number;
  exitPrice: Num;
  profit: Num;
  profitPercent: Num;
  commission: Num;
  runUp: Num;
  drawdown: Num;
  entryComment: string | null;
  exitComment: string | null;
}

export interface OpenTrade {
  entryId: string;
  direction: Direction;
  entryBar: number;
  entryTime: number;
  entryPrice: Num;
  quantity: Num;
}

export interface EquityPoint {
  bar: number;
  time: number;
  close: Num;
  equity: Num;
  positionSize: Num;
  openProfit: Num;
  drawdown: Num;
}

export interface PlotSeries { title: string; values: (Num | null)[] }

export type Metrics = Record<string, Num | null>;

export interface BacktestResponse {
  settings: Record<string, unknown>;
  metrics: Metrics;
  inputs: Record<string, string>;
  trades: ClosedTrade[];
  openTrades: OpenTrade[];
  equity: EquityPoint[];
  plots: PlotSeries[];
  bars: Bar[];
}

export interface BacktestError {
  stage?: string;
  message: string;
  line?: number;
  column?: number;
  bar?: number;
}

export interface RunRequest {
  script: string;
  data?: File | null;
  inputs?: string;
  minTick?: number;
}

export const num = (value: Num | null | undefined): number =>
  value == null ? NaN : typeof value === 'number' ? value : Number(value);

export async function runBacktest(request: RunRequest): Promise<BacktestResponse> {
  const form = new FormData();
  form.append('script', new Blob([request.script], { type: 'text/plain' }), 'strategy.pine');
  if (request.data) form.append('data', request.data);
  if (request.inputs?.trim()) form.append('inputs', request.inputs);
  if (request.minTick) form.append('minTick', String(request.minTick));

  const response = await fetch('/api/backtest', { method: 'POST', body: form });
  if (!response.ok) {
    const body = await response.json().catch(() => ({ message: `HTTP ${response.status}` }));
    throw body as BacktestError;
  }
  return response.json();
}

export async function loadSample(name: string): Promise<string> {
  const response = await fetch(`/api/backtest/samples/${name}`);
  if (!response.ok) throw { message: `Esempio ${name} non trovato` } as BacktestError;
  return response.text();
}
