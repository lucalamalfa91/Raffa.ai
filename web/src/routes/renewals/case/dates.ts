/**
 * Calendar helpers for the case engine. Dates are display strings in the contract's own format
 * ("08 Sep 2026"); arithmetic runs in UTC so results never depend on the machine's time zone or DST.
 */

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"] as const;
const DAY_MS = 86_400_000;

/** "DD Mon YYYY", e.g. "08 Sep 2026". */
export type CaseDate = string;

function parse(date: CaseDate): Date {
  const match = /^(\d+) (\w+) (\d+)$/.exec(date.trim());
  const month = match ? MONTHS.indexOf(match[2] as (typeof MONTHS)[number]) : -1;
  if (!match || month < 0) throw new Error(`Unrecognised case date: "${date}"`);
  return new Date(Date.UTC(Number(match[3]), month, Number(match[1])));
}

function format(date: Date): CaseDate {
  const day = date.getUTCDate();
  return `${day < 10 ? "0" : ""}${day} ${MONTHS[date.getUTCMonth()]} ${date.getUTCFullYear()}`;
}

export function addDays(date: CaseDate, days: number): CaseDate {
  const d = parse(date);
  d.setUTCDate(d.getUTCDate() + days);
  return format(d);
}

export function addMonths(date: CaseDate, months: number): CaseDate {
  const d = parse(date);
  d.setUTCMonth(d.getUTCMonth() + months);
  return format(d);
}

/** Whole days from `today` to `date` (negative when `date` has passed). */
export function daysBetween(today: CaseDate, date: CaseDate): number {
  return Math.round((parse(date).getTime() - parse(today).getTime()) / DAY_MS);
}

export function isAfter(a: CaseDate, b: CaseDate): boolean {
  return parse(a).getTime() > parse(b).getTime();
}

/** "2026-10-02" (what the API sends) → "02 Oct 2026". */
export function isoToCaseDate(iso: string): CaseDate {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  if (!match) throw new Error(`Not an ISO date: "${iso}"`);
  return format(new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]))));
}
