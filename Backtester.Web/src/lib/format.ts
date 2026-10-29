import { num, type Num } from './api';

const number = (digits: number) =>
  new Intl.NumberFormat('it-IT', { minimumFractionDigits: digits, maximumFractionDigits: digits });

export function money(value: Num | null | undefined, digits = 2): string {
  const n = num(value);
  return Number.isFinite(n) ? number(digits).format(n) : '—';
}

export function percent(value: Num | null | undefined, digits = 2): string {
  const n = num(value);
  return Number.isFinite(n) ? `${number(digits).format(n)}%` : '—';
}

/** Bar times are unix milliseconds, shown in UTC like the data. */
export function dateTime(ms: number): string {
  return new Date(ms).toISOString().slice(0, 16).replace('T', ' ');
}

export const sign = (value: Num | null | undefined) => {
  const n = num(value);
  return n > 0 ? 'text-up' : n < 0 ? 'text-down' : '';
};
