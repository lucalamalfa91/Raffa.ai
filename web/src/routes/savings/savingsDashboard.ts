import type { PortfolioPageBody, SavingsOpportunityBody } from "../../api/client";
import { isDeadlineCritical } from "../../styles/semantics";
import { daysUntil } from "../contracts/portfolioAttention";
import { formatPortfolioDate } from "../contracts/portfolioTableFormatters";
import { formatCompactAmount } from "../contracts/portfolioViewModel";

/**
 * Pure view-model for the Savings dashboard (route `/savings`): the pipeline bar, "When you saved"
 * (money verified per month), "Where the savings are" (by supplier / by lever) and "Act before the
 * notice deadline". No React here -- every rule is unit-testable (`savingsDashboard.test.ts`).
 *
 * **One currency at a time.** There is no conversion service, so nothing here ever sums across
 * currencies (the same discipline `savingsViewModel.ts#formatCurrencyAmount` states): every figure
 * is computed for the one `currency` the caller passes, and the screen offers a switch when the
 * tenant's opportunities carry more than one (`listDashboardCurrencies`).
 *
 * **Estimates vs money.** Identified and in-progress savings are estimate *ranges*; verified savings
 * are recorded money (`realizedAmount` on a `Realized` opportunity). Where a chart needs one number
 * per range it uses the range's midpoint and says so on screen (`MIDPOINT_NOTE`); the ranges
 * themselves stay in the labels and the table. A realized opportunity with no recorded amount is
 * counted, never valued at a guess.
 *
 * **When.** The opportunities list carries no separate "realized at": a realized opportunity is
 * frozen (the generator stops refreshing it once it leaves `Identified`), so its `updatedAt` is when
 * its outcome was recorded -- the month "When you saved" files it under. Identified savings are
 * filed under `createdAt`, the month Raffa.ai found them.
 */

const INTEGER_FORMAT = new Intl.NumberFormat("en-GB");
// Fixed English tables, never `Intl` month/compact names: those vary by browser ICU ("Sep"/"Sept",
// "85K"/"85k"), and every other screen already reads the same on every browser.
const MONTH_SHORT = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"] as const;
const MONTH_LONG = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"] as const;

export const MIDPOINT_NOTE = "Estimates are sized at the midpoint of their range; verified savings are recorded money.";

export type SavingsStage = "identified" | "inProgress" | "verified";

/** Stage order, labels and the status each one filters the table to -- shared by every chart so the three colours always mean the same thing. */
export const SAVINGS_STAGES: ReadonlyArray<{ key: SavingsStage; label: string; status: SavingsOpportunityBody["status"] }> = [
  { key: "identified", label: "Identified", status: "Identified" },
  { key: "inProgress", label: "In progress", status: "InProgress" },
  { key: "verified", label: "Verified", status: "Realized" },
];

export function formatMoney(currency: string, amount: number): string {
  return `${currency} ${INTEGER_FORMAT.format(Math.round(amount))}`;
}

/** Axis ticks, bar-end labels and tooltips: "CHF 85k", "CHF 1.2M" -- Portfolio's own compact figure (`formatCompactAmount`). */
export function formatCompactMoney(currency: string, amount: number): string {
  return formatCompactAmount(amount, currency);
}

export function formatCompactRange(currency: string, low: number, high: number): string {
  const lowText = formatCompactAmount(low, null);
  const highText = formatCompactAmount(high, null);
  return lowText === highText ? `${currency} ${lowText}` : `${currency} ${lowText}–${highText}`;
}

/** "0.4%" under 10%, "12%" from there -- a share of spend, never more precision than the estimate has. */
export function formatPercent(ratio: number): string {
  const percent = ratio * 100;
  if (percent > 0 && percent < 10) return `${percent.toFixed(1)}%`;
  return `${Math.round(percent)}%`;
}

function midpoint(item: SavingsOpportunityBody): number {
  return (item.estimatedSavingsLow + item.estimatedSavingsHigh) / 2;
}

function isOpen(item: SavingsOpportunityBody): boolean {
  return item.status === "Identified" || item.status === "InProgress";
}

/** Verified money on a realized opportunity, or `null` when none was recorded (counted, never guessed). */
function verifiedAmount(item: SavingsOpportunityBody): number | null {
  return item.status === "Realized" && item.realizedAmount !== null ? item.realizedAmount : null;
}

function stageOf(item: SavingsOpportunityBody): SavingsStage {
  return item.status === "Realized" ? "verified" : item.status === "InProgress" ? "inProgress" : "identified";
}

/**
 * The currencies the tenant's opportunities carry, largest money first (an open saving's estimate
 * high, a realized one's verified money), so the dashboard opens on the currency that matters most.
 * Ties keep first-seen order.
 */
export function listDashboardCurrencies(items: readonly SavingsOpportunityBody[]): readonly string[] {
  const weight = new Map<string, number>();
  for (const item of items) {
    const money = item.status === "Realized" ? (verifiedAmount(item) ?? item.estimatedSavingsHigh) : item.estimatedSavingsHigh;
    weight.set(item.currency, (weight.get(item.currency) ?? 0) + money);
  }
  return Array.from(weight.entries())
    .sort((a, b) => b[1] - a[1])
    .map(([currency]) => currency);
}

// ---------------------------------------------------------------------------------------------
// Pipeline: identified -> in progress -> verified
// ---------------------------------------------------------------------------------------------

export interface PipelineSegmentView {
  key: SavingsStage;
  label: string;
  status: SavingsOpportunityBody["status"];
  count: number;
  /** Midpoint for estimate stages, recorded money for verified -- what the segment's width is sized by. */
  value: number;
  /** The segment's share of the whole bar, 0..1. */
  share: number;
  /** "CHF 80k–120k" for an estimate stage, "CHF 85k" for verified; "—" when the stage is empty. */
  amountLabel: string;
  countLabel: string;
}

export interface PipelineView {
  currency: string;
  segments: readonly PipelineSegmentView[];
  total: number;
  /** "12% of the pipeline is already verified money", or `null` while nothing is verified. */
  verifiedShareLine: string | null;
}

function countLabel(count: number): string {
  return `${count} opportunit${count === 1 ? "y" : "ies"}`;
}

/** `null` when the currency has no opportunity at all -- the section then shows its own empty line, never an all-zero bar. */
export function buildSavingsPipeline(items: readonly SavingsOpportunityBody[], currency: string): PipelineView | null {
  const scoped = items.filter((item) => item.currency === currency);
  if (scoped.length === 0) return null;

  const segments = SAVINGS_STAGES.map(({ key, label, status }) => {
    const inStage = scoped.filter((item) => stageOf(item) === key);
    if (key === "verified") {
      const amounts = inStage.map(verifiedAmount).filter((amount): amount is number => amount !== null);
      const value = amounts.reduce((total, amount) => total + amount, 0);
      return {
        key,
        label,
        status,
        count: inStage.length,
        value,
        share: 0,
        amountLabel: amounts.length === 0 ? "—" : formatCompactMoney(currency, value),
        countLabel: countLabel(inStage.length),
      };
    }
    const low = inStage.reduce((total, item) => total + item.estimatedSavingsLow, 0);
    const high = inStage.reduce((total, item) => total + item.estimatedSavingsHigh, 0);
    return {
      key,
      label,
      status,
      count: inStage.length,
      value: inStage.reduce((total, item) => total + midpoint(item), 0),
      share: 0,
      amountLabel: inStage.length === 0 ? "—" : formatCompactRange(currency, low, high),
      countLabel: countLabel(inStage.length),
    };
  });

  const total = segments.reduce((sum, segment) => sum + segment.value, 0);
  const withShares = segments.map((segment) => ({ ...segment, share: total > 0 ? segment.value / total : 0 }));
  const verified = withShares.find((segment) => segment.key === "verified");
  const verifiedShareLine =
    verified !== undefined && verified.value > 0 ? `${formatPercent(verified.share)} of the pipeline is already verified money` : null;

  return { currency, segments: withShares, total, verifiedShareLine };
}

// ---------------------------------------------------------------------------------------------
// When you saved: money verified (and savings found) per month
// ---------------------------------------------------------------------------------------------

export interface MonthBucketView {
  /** "2026-09" (UTC). */
  key: string;
  /** "Sep" -- the axis label. */
  label: string;
  /** "September 2026" -- tooltip and table. */
  longLabel: string;
  verified: number;
  verifiedCount: number;
  /** Midpoint of the savings Raffa.ai identified that month (any status today). */
  identified: number;
  identifiedCount: number;
  /** Verified money up to and including this month, within the window. */
  cumulativeVerified: number;
}

function monthKey(date: Date): string {
  return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, "0")}`;
}

function parseInstant(iso: string): Date | null {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? null : date;
}

/** The last `months` calendar months (UTC) ending with `now`'s month, oldest first -- always the full window, empty months included, so the axis never skips time. */
export function buildMonthlySavings(
  items: readonly SavingsOpportunityBody[],
  currency: string,
  now: Date = new Date(),
  months = 12,
): readonly MonthBucketView[] {
  const buckets: MonthBucketView[] = [];
  const index = new Map<string, MonthBucketView>();
  for (let offset = months - 1; offset >= 0; offset--) {
    const start = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - offset, 1));
    const bucket: MonthBucketView = {
      key: monthKey(start),
      label: MONTH_SHORT[start.getUTCMonth()],
      longLabel: `${MONTH_LONG[start.getUTCMonth()]} ${start.getUTCFullYear()}`,
      verified: 0,
      verifiedCount: 0,
      identified: 0,
      identifiedCount: 0,
      cumulativeVerified: 0,
    };
    buckets.push(bucket);
    index.set(bucket.key, bucket);
  }

  for (const item of items) {
    if (item.currency !== currency) continue;
    const created = parseInstant(item.createdAt);
    const createdBucket = created === null ? undefined : index.get(monthKey(created));
    if (createdBucket !== undefined) {
      createdBucket.identified += midpoint(item);
      createdBucket.identifiedCount += 1;
    }
    const amount = verifiedAmount(item);
    const recorded = parseInstant(item.updatedAt);
    const recordedBucket = recorded === null ? undefined : index.get(monthKey(recorded));
    if (amount !== null && recordedBucket !== undefined) {
      recordedBucket.verified += amount;
      recordedBucket.verifiedCount += 1;
    }
  }

  let running = 0;
  for (const bucket of buckets) {
    running += bucket.verified;
    bucket.cumulativeVerified = running;
  }
  return buckets;
}

export interface VerifiedStatView {
  key: "this-year" | "last-90-days" | "last-verified";
  label: string;
  value: string;
  meta: string;
}

export interface VerifiedSavingView {
  key: string;
  /** "12 Aug 2026" -- the day the outcome was recorded. */
  date: string;
  supplierLabel: string;
  lever: string;
  amount: string;
  contractId: string | null;
}

/** Resolves an opportunity's supplier exactly as the opportunities table does (`savingsViewModel.ts#resolveOpportunitySupplier`), so a name in a chart always matches a name in the table. */
export type SupplierLabelOf = (item: SavingsOpportunityBody) => string;

function verifiedByRecency(items: readonly SavingsOpportunityBody[], currency: string) {
  return items
    .filter((item) => item.currency === currency && verifiedAmount(item) !== null && parseInstant(item.updatedAt) !== null)
    .sort((a, b) => parseInstant(b.updatedAt)!.getTime() - parseInstant(a.updatedAt)!.getTime());
}

/** The latest verified savings, newest first -- the "when" behind the chart, one row per recorded outcome. */
export function buildRecentVerified(
  items: readonly SavingsOpportunityBody[],
  currency: string,
  supplierLabel: SupplierLabelOf,
  limit = 5,
): readonly VerifiedSavingView[] {
  return verifiedByRecency(items, currency)
    .slice(0, limit)
    .map((item) => ({
      key: item.id,
      date: formatPortfolioDate(item.updatedAt.slice(0, 10)),
      supplierLabel: supplierLabel(item),
      lever: item.type,
      amount: formatMoney(currency, verifiedAmount(item)!),
      contractId: item.contractId,
    }));
}

/**
 * The three "when" figures above the chart: verified since 1 January, verified in the last 90 days,
 * and the most recent verified saving. "—" with an honest meta when there is nothing to show.
 */
export function buildVerifiedStats(
  items: readonly SavingsOpportunityBody[],
  currency: string,
  supplierLabel: SupplierLabelOf,
  now: Date = new Date(),
): readonly VerifiedStatView[] {
  const verified = verifiedByRecency(items, currency);
  const yearStart = Date.UTC(now.getUTCFullYear(), 0, 1);
  const ninetyDaysAgo = now.getTime() - 90 * 24 * 60 * 60 * 1000;
  const sumSince = (since: number) => {
    const inWindow = verified.filter((item) => parseInstant(item.updatedAt)!.getTime() >= since);
    return { amount: inWindow.reduce((total, item) => total + verifiedAmount(item)!, 0), count: inWindow.length };
  };
  const year = sumSince(yearStart);
  const quarter = sumSince(ninetyDaysAgo);
  const latest = verified[0];
  const outcomes = (count: number) => `${count} recorded outcome${count === 1 ? "" : "s"}`;

  return [
    {
      key: "this-year",
      label: `Verified in ${now.getUTCFullYear()}`,
      value: year.count === 0 ? "—" : formatMoney(currency, year.amount),
      meta: year.count === 0 ? "nothing verified this year yet" : outcomes(year.count),
    },
    {
      key: "last-90-days",
      label: "Last 90 days",
      value: quarter.count === 0 ? "—" : formatMoney(currency, quarter.amount),
      meta: quarter.count === 0 ? "nothing verified in the last 90 days" : outcomes(quarter.count),
    },
    {
      key: "last-verified",
      label: "Last verified saving",
      value: latest === undefined ? "—" : formatPortfolioDate(latest.updatedAt.slice(0, 10)),
      meta: latest === undefined ? "no verified savings recorded yet" : `${supplierLabel(latest)} · ${formatMoney(currency, verifiedAmount(latest)!)}`,
    },
  ];
}

// ---------------------------------------------------------------------------------------------
// Where the savings are: by supplier / by lever
// ---------------------------------------------------------------------------------------------

export type BreakdownDimension = "supplier" | "lever";

export interface BreakdownRowView {
  key: string;
  label: string;
  /** Contract 360 target when every opportunity in the row points at the same contract. */
  contractId: string | null;
  /** Per-stage values (midpoints / money), in `SAVINGS_STAGES` order. */
  values: Readonly<Record<SavingsStage, number>>;
  total: number;
  /** 0..1 of the largest row -- the bar's length. */
  scale: number;
  /** "CHF 180k" -- the bar-end label (midpoint of open savings + verified money). */
  totalLabel: string;
  opportunityCount: number;
  /** True for the folded tail ("Other suppliers"), which has no single filter value. */
  isOther: boolean;
}

export const BREAKDOWN_LIMIT = 6;

/**
 * Open (identified + in progress, at midpoint) and verified money per supplier or per lever, largest
 * first. Past `limit` rows the tail folds into one "Other" row -- never an unreadable long list.
 * `supplierLabel` resolves a row's supplier exactly as the opportunities table does, so a click on a
 * bar can filter the table to the same label.
 */
export function buildSavingsBreakdown(
  items: readonly SavingsOpportunityBody[],
  currency: string,
  dimension: BreakdownDimension,
  supplierLabel: SupplierLabelOf,
  limit = BREAKDOWN_LIMIT,
): readonly BreakdownRowView[] {
  const groups = new Map<string, { label: string; contractIds: Set<string | null>; values: Record<SavingsStage, number>; count: number }>();
  for (const item of items) {
    if (item.currency !== currency) continue;
    const label = dimension === "supplier" ? supplierLabel(item) : item.type;
    const group = groups.get(label) ?? { label, contractIds: new Set(), values: { identified: 0, inProgress: 0, verified: 0 }, count: 0 };
    const stage = stageOf(item);
    group.values[stage] += stage === "verified" ? (verifiedAmount(item) ?? 0) : midpoint(item);
    group.contractIds.add(item.contractId);
    group.count += 1;
    groups.set(label, group);
  }

  const total = (values: Record<SavingsStage, number>) => values.identified + values.inProgress + values.verified;
  const sorted = Array.from(groups.values()).sort((a, b) => total(b.values) - total(a.values));
  const head = sorted.length > limit ? sorted.slice(0, limit - 1) : sorted;
  const tail = sorted.length > limit ? sorted.slice(limit - 1) : [];

  const rows = head.map((group) => {
    const onlyContract = group.contractIds.size === 1 ? Array.from(group.contractIds)[0] : null;
    return { key: `${dimension}:${group.label}`, label: group.label, contractId: dimension === "supplier" ? onlyContract : null, values: group.values, count: group.count, isOther: false };
  });
  if (tail.length > 0) {
    const values = { identified: 0, inProgress: 0, verified: 0 };
    for (const group of tail) {
      values.identified += group.values.identified;
      values.inProgress += group.values.inProgress;
      values.verified += group.values.verified;
    }
    rows.push({
      key: `${dimension}:__other`,
      label: `Other ${dimension === "supplier" ? "suppliers" : "levers"} (${tail.length})`,
      contractId: null,
      values,
      count: tail.reduce((sum, group) => sum + group.count, 0),
      isOther: true,
    });
  }

  const max = rows.reduce((largest, row) => Math.max(largest, total(row.values)), 0);
  return rows.map((row) => ({
    key: row.key,
    label: row.label,
    contractId: row.contractId,
    values: row.values,
    total: total(row.values),
    scale: max > 0 ? total(row.values) / max : 0,
    totalLabel: formatCompactMoney(currency, total(row.values)),
    opportunityCount: row.count,
    isOther: row.isOther,
  }));
}

// ---------------------------------------------------------------------------------------------
// Act before the notice deadline: open savings whose contract still has a notice date ahead
// ---------------------------------------------------------------------------------------------

export interface DeadlineQueueRowView {
  key: string;
  contractId: string;
  supplierLabel: string;
  lever: string;
  estimate: string;
  /** "12 Oct 2026" -- the Portfolio's own date format for the same deadline. */
  deadline: string;
  daysToNotice: number;
  /** "in 14 days" / "today". */
  daysLabel: string;
  /** Inside the locked 45-day window (`isDeadlineCritical`): accent + weight, paired with the text. */
  isUrgent: boolean;
  statusLabel: string;
}

/** How far ahead the queue looks: two notice cycles of the 90-day threshold Renewals works to. */
export const DEADLINE_QUEUE_HORIZON_DAYS = 180;

/**
 * Open opportunities (identified or in progress) on a contract whose cancellation deadline -- from
 * the portfolio row, the same date Portfolio and Renewals show -- is today or ahead, within
 * `DEADLINE_QUEUE_HORIZON_DAYS`, soonest first. This is the money that is lost if the notice date
 * passes, so it leads to Renewals, where the notice is worked.
 */
export function buildDeadlineQueue(
  items: readonly SavingsOpportunityBody[],
  portfolio: PortfolioPageBody["items"],
  supplierLabel: SupplierLabelOf,
  now: Date = new Date(),
  horizonDays = DEADLINE_QUEUE_HORIZON_DAYS,
): readonly DeadlineQueueRowView[] {
  const deadlines = new Map<string, string>();
  for (const contract of portfolio) {
    if (contract.cancellationDeadline !== null) deadlines.set(contract.contractId, contract.cancellationDeadline);
  }

  const rows: DeadlineQueueRowView[] = [];
  for (const item of items) {
    if (!isOpen(item) || item.contractId === null) continue;
    const deadline = deadlines.get(item.contractId);
    if (deadline === undefined) continue;
    const days = daysUntil(deadline, now);
    if (days === null || days < 0 || days > horizonDays) continue;
    rows.push({
      key: item.id,
      contractId: item.contractId,
      supplierLabel: supplierLabel(item),
      lever: item.type,
      estimate: formatCompactRange(item.currency, item.estimatedSavingsLow, item.estimatedSavingsHigh),
      deadline: formatPortfolioDate(deadline),
      daysToNotice: days,
      daysLabel: days === 0 ? "today" : `in ${days} day${days === 1 ? "" : "s"}`,
      isUrgent: isDeadlineCritical(days),
      statusLabel: item.status === "InProgress" ? "In progress" : "Identified",
    });
  }
  return rows.sort((a, b) => a.daysToNotice - b.daysToNotice || (a.key < b.key ? -1 : a.key > b.key ? 1 : 0));
}

/** contractId -> days to its notice deadline (today or later), for the table's "Notice in" column. */
export function buildNoticeIndex(portfolio: PortfolioPageBody["items"], now: Date = new Date()): ReadonlyMap<string, number> {
  const index = new Map<string, number>();
  for (const contract of portfolio) {
    const days = daysUntil(contract.cancellationDeadline, now);
    if (days !== null && days >= 0) index.set(contract.contractId, days);
  }
  return index;
}

// ---------------------------------------------------------------------------------------------
// Chart scale
// ---------------------------------------------------------------------------------------------

/** The smallest "clean" number (1, 2, 2.5, 5 × 10^n) at or above `value` -- the chart's top tick. */
export function niceCeiling(value: number): number {
  if (!(value > 0)) return 0;
  const base = 10 ** Math.floor(Math.log10(value));
  for (const step of [1, 2, 2.5, 5, 10]) {
    if (value <= step * base + 1e-9) return step * base;
  }
  return 10 * base;
}

export interface ChartScale {
  max: number;
  /** Bottom to top: 0, half, max -- three recessive gridlines, never more. */
  ticks: readonly number[];
}

/** One y-scale for both series of "When you saved" (never a second axis). `max === 0` means an empty chart. */
export function buildMonthlyScale(buckets: readonly MonthBucketView[]): ChartScale {
  const peak = buckets.reduce((largest, bucket) => Math.max(largest, bucket.verified, bucket.identified), 0);
  const max = niceCeiling(peak);
  return { max, ticks: max === 0 ? [0] : [0, max / 2, max] };
}

/** The month the chart direct-labels: the largest verified month, or `null` when nothing was verified in the window. */
export function findPeakVerifiedMonth(buckets: readonly MonthBucketView[]): string | null {
  let peak: MonthBucketView | null = null;
  for (const bucket of buckets) {
    if (bucket.verified > 0 && (peak === null || bucket.verified >= peak.verified)) peak = bucket;
  }
  return peak?.key ?? null;
}
