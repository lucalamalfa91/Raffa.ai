import type { SpendContractRow, SpendObjectiveEntry, SpendReport, SpendSavingEntry, SpendSegmentKind } from "../../api/spend";
import { formatCompactAmount } from "../contracts/portfolioViewModel";

/**
 * Spesa e risparmi, the pure half of the page: everything it shows is read from
 * `GET /api/savings/spend` (values per fiscal quarter) and summed here for the filters in the URL.
 * No figure is made up here: sums, shares and day counts only.
 */

export type SpendView = "year" | "quarter";
/** committed: actual + committed · auto: + renewing by itself · all: + to decide. */
export type SpendInclude = "committed" | "auto" | "all";
export type SpendSort = "spend" | "savings" | "supplier" | "decision";

export interface SpendFilters {
  view: SpendView;
  /** The fiscal year the page is on; null: the current one. */
  year: number | null;
  basis: "accrual" | "cash";
  include: SpendInclude;
  /** A category, "" for contracts without one, null for all. */
  category: string | null;
  sort: SpendSort;
  /** The contract whose detail is open. */
  contract: string | null;
}

export const DEFAULT_FILTERS: SpendFilters = {
  view: "year",
  year: null,
  basis: "accrual",
  include: "all",
  category: null,
  sort: "spend",
  contract: null,
};

/** The filters a URL carries (`?view=quarter&year=2027&basis=cash&include=auto&cat=CRM&sort=decision&contract=…`). */
export function readFilters(params: URLSearchParams): SpendFilters {
  const year = Number(params.get("year"));
  const pick = <T extends string>(value: string | null, allowed: readonly T[], fallback: T): T =>
    value !== null && (allowed as readonly string[]).includes(value) ? (value as T) : fallback;
  return {
    view: pick(params.get("view"), ["year", "quarter"] as const, DEFAULT_FILTERS.view),
    year: Number.isInteger(year) && year >= 1990 && year <= 2100 ? year : null,
    basis: pick(params.get("basis"), ["accrual", "cash"] as const, DEFAULT_FILTERS.basis),
    include: pick(params.get("include"), ["committed", "auto", "all"] as const, DEFAULT_FILTERS.include),
    category: params.has("cat") ? params.get("cat") : null,
    sort: pick(params.get("sort"), ["spend", "savings", "supplier", "decision"] as const, DEFAULT_FILTERS.sort),
    contract: params.get("contract"),
  };
}

/** The URL of <paramref>filters</paramref>, defaults left out (so a plain `/savings` is the default view). */
export function writeFilters(filters: SpendFilters): URLSearchParams {
  const out = new URLSearchParams();
  if (filters.view !== DEFAULT_FILTERS.view) out.set("view", filters.view);
  if (filters.year !== null) out.set("year", String(filters.year));
  if (filters.basis !== DEFAULT_FILTERS.basis) out.set("basis", filters.basis);
  if (filters.include !== DEFAULT_FILTERS.include) out.set("include", filters.include);
  if (filters.category !== null) out.set("cat", filters.category);
  if (filters.sort !== DEFAULT_FILTERS.sort) out.set("sort", filters.sort);
  if (filters.contract) out.set("contract", filters.contract);
  return out;
}

/** "CHF 281k" / "EUR 1.2M"; "—" for nothing. */
export function money(value: number, currency: string): string {
  return Math.abs(value) < 0.5 ? "—" : formatCompactAmount(Math.round(value), currency);
}

/** A table cell: "281k", "—". */
export function cell(value: number): string {
  if (Math.abs(value) < 0.5) return "—";
  return Math.abs(value) >= 999_500 ? `${(value / 1_000_000).toFixed(2)}M` : `${Math.round(value / 1000)}k`;
}

const sumAt = (values: readonly number[], idx: readonly number[]): number => idx.reduce((s, i) => s + (values[i] ?? 0), 0);

/** The spend of a row in the periods <paramref>idx</paramref>, as far as <paramref>include</paramref> goes. */
export function spendOf(row: SpendContractRow, idx: readonly number[], include: SpendInclude): number {
  return (
    sumAt(row.actual, idx) +
    sumAt(row.committed, idx) +
    (include !== "committed" ? sumAt(row.autoRenewal, idx) : 0) +
    (include === "all" ? sumAt(row.toDecide, idx) : 0)
  );
}

/** Every kind, whatever the filter: what the contracts run on if nothing changes. */
export const allSpendOf = (row: SpendContractRow, idx: readonly number[]): number => spendOf(row, idx, "all");

export function yearIndexes(report: SpendReport, fiscalYear: number): number[] {
  return report.periods.flatMap((p, i) => (p.fiscalYear === fiscalYear ? [i] : []));
}

export function selectedYear(report: SpendReport, filters: SpendFilters): number {
  const years = report.fiscalYears.map((y) => y.fiscalYear);
  if (filters.year !== null && years.includes(filters.year)) return filters.year;
  return years.includes(report.currentFiscalYear) ? report.currentFiscalYear : (years[0] ?? report.currentFiscalYear);
}

export function filteredRows(report: SpendReport, filters: SpendFilters): SpendContractRow[] {
  return filters.category === null ? report.contracts : report.contracts.filter((r) => (r.category ?? "") === filters.category);
}

/** The categories present, contracts without one under "" (last). */
export function categories(report: SpendReport): string[] {
  const set = new Set(report.contracts.map((r) => r.category ?? ""));
  return [...set].sort((a, b) => (a === "" ? 1 : b === "" ? -1 : a.localeCompare(b)));
}

const dayNumber = (iso: string): number => Math.floor(Date.parse(iso + "T00:00:00Z") / 86_400_000);
export const daysBetween = (fromIso: string, toIso: string): number => dayNumber(toIso) - dayNumber(fromIso);

export interface ChartColumn {
  key: string;
  label: string;
  fiscalYear: number;
  current: boolean;
  selected: boolean;
  actual: number;
  committed: number;
  autoRenewal: number;
  toDecide: number;
  total: number;
  /** Savings proven by invoices in the period. */
  proven: number;
  /** Signed savings of the period not (yet) proven by invoices. */
  signedOnly: number;
  /** The open cases' objectives in the period (user targets). */
  objective: number;
}

/** The bars: one per fiscal year, or one per quarter of the three fiscal years around the selected one. */
export function chartColumns(report: SpendReport, filters: SpendFilters): ChartColumn[] {
  const rows = filteredRows(report, filters);
  const year = selectedYear(report, filters);
  const other = filters.category === null ? report.otherSavings : null;
  const column = (key: string, label: string, fiscalYear: number, idx: number[], current: boolean): ChartColumn => {
    const contractual = rows.reduce((s, r) => s + sumAt(r.savingsContractual, idx), 0) + (other ? sumAt(other.savingsContractual, idx) : 0);
    const realized = rows.reduce((s, r) => s + sumAt(r.savingsRealized, idx), 0) + (other ? sumAt(other.savingsRealized, idx) : 0);
    const actual = rows.reduce((s, r) => s + sumAt(r.actual, idx), 0);
    const committed = rows.reduce((s, r) => s + sumAt(r.committed, idx), 0);
    const autoRenewal = filters.include !== "committed" ? rows.reduce((s, r) => s + sumAt(r.autoRenewal, idx), 0) : 0;
    const toDecide = filters.include === "all" ? rows.reduce((s, r) => s + sumAt(r.toDecide, idx), 0) : 0;
    return {
      key,
      label,
      fiscalYear,
      current,
      selected: fiscalYear === year,
      actual,
      committed,
      autoRenewal,
      toDecide,
      total: actual + committed + autoRenewal + toDecide,
      proven: realized,
      signedOnly: Math.max(0, contractual - realized),
      objective: rows.reduce((s, r) => s + sumAt(r.objective, idx), 0),
    };
  };
  if (filters.view === "year") {
    return report.fiscalYears.map((y) =>
      column(String(y.fiscalYear), y.label, y.fiscalYear, yearIndexes(report, y.fiscalYear), y.fiscalYear === report.currentFiscalYear),
    );
  }
  const years = report.fiscalYears.map((y) => y.fiscalYear);
  const first = Math.max(years[0] ?? year, Math.min(year - 1, (years[years.length - 1] ?? year) - 2));
  return report.periods.flatMap((p, i) =>
    p.fiscalYear >= first && p.fiscalYear <= first + 2
      ? [column(p.key, p.label, p.fiscalYear, [i], p.from <= report.today && report.today <= p.to)]
      : [],
  );
}

export interface SpendKpis {
  year: number;
  yearLabel: string;
  spend: number;
  actualToDate: number;
  previous: number | null;
  /** null when the previous year is not comparable (fewer documented contracts). */
  changePct: number | null;
  previousDocumented: number;
  documented: number;
  contracts: number;
  savingsSigned: number;
  savingsProven: number;
  quantityReduction: number;
  nextYear: number | null;
  nextLabel: string | null;
  nextCommitted: number;
  nextTotal: number;
  decideSoon: number;
  decideSoonCount: number;
  firstDecision: { supplier: string; on: string } | null;
}

/** The current annual value of a row (the stretch running today, else the last documented one). */
export function currentAnnual(report: SpendReport, row: SpendContractRow): number | null {
  const running = row.segments.find((s) => s.from <= report.today && report.today <= s.to);
  return running?.annualValue ?? [...row.segments].reverse().find((s) => s.kind === "documented")?.annualValue ?? null;
}

export function decisionDay(row: SpendContractRow): string | null {
  return row.decision.decideBy ?? row.decision.endsOn;
}

export function kpis(report: SpendReport, filters: SpendFilters): SpendKpis {
  const rows = filteredRows(report, filters);
  const year = selectedYear(report, filters);
  const idx = yearIndexes(report, year);
  const fy = report.fiscalYears.find((y) => y.fiscalYear === year);
  const prevFy = report.fiscalYears.find((y) => y.fiscalYear === year - 1);
  const nextFy = report.fiscalYears.find((y) => y.fiscalYear === year + 1);
  const spend = rows.reduce((s, r) => s + spendOf(r, idx, filters.include), 0);
  const previous = prevFy ? rows.reduce((s, r) => s + spendOf(r, yearIndexes(report, year - 1), filters.include), 0) : null;
  const comparable = prevFy !== undefined && fy !== undefined && prevFy.documented >= fy.documented && (previous ?? 0) > 0;
  const nextIdx = nextFy ? yearIndexes(report, year + 1) : [];
  const soon = rows
    .map((r) => ({ r, day: decisionDay(r) }))
    .filter((x): x is { r: SpendContractRow; day: string } => x.day !== null && x.day >= report.today && daysBetween(report.today, x.day) <= 365)
    .sort((a, b) => a.day.localeCompare(b.day));
  const other = filters.category === null ? report.otherSavings : null;
  return {
    year,
    yearLabel: fy?.label ?? `FY ${year}`,
    spend,
    actualToDate: rows.reduce((s, r) => s + sumAt(r.actual, idx), 0),
    previous,
    changePct: comparable && previous ? ((spend - previous) / previous) * 100 : null,
    previousDocumented: prevFy?.documented ?? 0,
    documented: fy?.documented ?? 0,
    contracts: fy?.contracts ?? rows.length,
    savingsSigned: rows.reduce((s, r) => s + sumAt(r.savingsContractual, idx), 0) + (other ? sumAt(other.savingsContractual, idx) : 0),
    savingsProven: rows.reduce((s, r) => s + sumAt(r.savingsRealized, idx), 0) + (other ? sumAt(other.savingsRealized, idx) : 0),
    quantityReduction: rows.reduce((s, r) => s + sumAt(r.quantityReduction, idx), 0) + (other ? sumAt(other.quantityReduction, idx) : 0),
    nextYear: nextFy ? year + 1 : null,
    nextLabel: nextFy?.label ?? null,
    nextCommitted: rows.reduce((s, r) => s + sumAt(r.actual, nextIdx) + sumAt(r.committed, nextIdx), 0),
    nextTotal: rows.reduce((s, r) => s + allSpendOf(r, nextIdx), 0),
    decideSoon: soon.reduce((s, x) => s + (currentAnnual(report, x.r) ?? 0), 0),
    decideSoonCount: soon.length,
    firstDecision: soon[0] ? { supplier: soon[0].r.supplier, on: soon[0].day } : null,
  };
}

export interface PivotColumn {
  key: string;
  label: string;
  idx: number[];
  /** The selected fiscal year (emphasised). */
  strong: boolean;
}

export interface PivotLine {
  contractId: string;
  supplier: string;
  name: string;
  values: number[];
  /** Quarter view: the change against the previous fiscal year ("nuovo"/"cessato" as keys). */
  delta: number | "new" | "ended" | null;
  /** Share of the selected year's spend of every contract. */
  share: number;
  savings: number;
  decisionDay: string | null;
  decisionDays: number | null;
  spend: number;
}

export interface PivotGroup {
  category: string;
  lines: PivotLine[];
  values: number[];
  share: number;
  savings: number;
  spend: number;
  decisionDays: number | null;
}

export interface Pivot {
  columns: PivotColumn[];
  groups: PivotGroup[];
  total: { values: number[]; share: number; savings: number; spend: number };
  deltaColumn: boolean;
}

export function pivot(report: SpendReport, filters: SpendFilters): Pivot {
  const year = selectedYear(report, filters);
  const idx = yearIndexes(report, year);
  const rows = filteredRows(report, filters);
  const grand = report.contracts.reduce((s, r) => s + spendOf(r, idx, filters.include), 0) || 1;
  const columns: PivotColumn[] =
    filters.view === "year"
      ? report.fiscalYears.map((y) => ({ key: String(y.fiscalYear), label: y.label, idx: yearIndexes(report, y.fiscalYear), strong: y.fiscalYear === year }))
      : [
          ...idx.map((i) => ({ key: report.periods[i]!.key, label: report.periods[i]!.label, idx: [i], strong: false })),
          { key: `fy-${year}`, label: report.fiscalYears.find((y) => y.fiscalYear === year)?.label ?? `FY ${year}`, idx, strong: true },
          ...(report.fiscalYears.some((y) => y.fiscalYear === year - 1)
            ? [{ key: `fy-${year - 1}`, label: report.fiscalYears.find((y) => y.fiscalYear === year - 1)!.label, idx: yearIndexes(report, year - 1), strong: false }]
            : []),
        ];
  const deltaColumn = filters.view === "quarter" && report.fiscalYears.some((y) => y.fiscalYear === year - 1);
  const prevIdx = yearIndexes(report, year - 1);
  const line = (r: SpendContractRow): PivotLine => {
    const spend = spendOf(r, idx, filters.include);
    const prev = spendOf(r, prevIdx, filters.include);
    const day = decisionDay(r);
    return {
      contractId: r.contractId,
      supplier: r.supplier,
      name: r.name,
      values: columns.map((c) => spendOf(r, c.idx, filters.include)),
      delta: !deltaColumn ? null : prev < 0.5 ? (spend < 0.5 ? null : "new") : spend < 0.5 ? "ended" : ((spend - prev) / prev) * 100,
      share: spend / grand,
      savings: sumAt(r.savingsContractual, idx),
      decisionDay: day,
      decisionDays: day ? daysBetween(report.today, day) : null,
      spend,
    };
  };
  const order = (a: PivotLine, b: PivotLine): number => {
    switch (filters.sort) {
      case "supplier":
        return a.supplier.localeCompare(b.supplier);
      case "decision":
        return (a.decisionDays ?? Number.MAX_SAFE_INTEGER) - (b.decisionDays ?? Number.MAX_SAFE_INTEGER);
      case "savings":
        return b.savings - a.savings;
      default:
        return b.spend - a.spend;
    }
  };
  const byCategory = new Map<string, SpendContractRow[]>();
  for (const r of rows) byCategory.set(r.category ?? "", [...(byCategory.get(r.category ?? "") ?? []), r]);
  const groups: PivotGroup[] = [...byCategory.entries()].map(([category, list]) => {
    const lines = list.map(line).sort(order);
    const days = lines.map((l) => l.decisionDays).filter((d): d is number => d !== null);
    return {
      category,
      lines,
      values: columns.map((_, i) => lines.reduce((s, l) => s + l.values[i]!, 0)),
      share: lines.reduce((s, l) => s + l.share, 0),
      savings: lines.reduce((s, l) => s + l.savings, 0),
      spend: lines.reduce((s, l) => s + l.spend, 0),
      decisionDays: days.length ? Math.min(...days) : null,
    };
  });
  groups.sort((a, b) => {
    switch (filters.sort) {
      case "supplier":
        return a.category.localeCompare(b.category);
      case "decision":
        return (a.decisionDays ?? Number.MAX_SAFE_INTEGER) - (b.decisionDays ?? Number.MAX_SAFE_INTEGER);
      case "savings":
        return b.savings - a.savings;
      default:
        return b.spend - a.spend;
    }
  });
  return {
    columns,
    groups,
    total: {
      values: columns.map((_, i) => groups.reduce((s, g) => s + g.values[i]!, 0)),
      share: groups.reduce((s, g) => s + g.share, 0),
      savings: groups.reduce((s, g) => s + g.savings, 0),
      spend: groups.reduce((s, g) => s + g.spend, 0),
    },
    deltaColumn,
  };
}

export interface TimelineBar {
  left: number;
  width: number;
  kind: SpendSegmentKind;
  from: string;
  to: string;
  annualValue: number | null;
}

export interface ContractDetail {
  row: SpendContractRow;
  share: number;
  categoryShare: number;
  spend: number;
  currentAnnual: number | null;
  running: { from: string; to: string } | null;
  lockedRenewal: { from: string; to: string; annualValue: number | null } | null;
  quarters: { label: string; actual: number; committed: number; autoRenewal: number; toDecide: number }[];
  timeline: TimelineBar[];
  ticks: { label: string; left: number }[];
  todayLeft: number;
  decideLeft: number | null;
  savings: SpendSavingEntry[];
  objective: SpendObjectiveEntry | null;
}

export function contractDetail(report: SpendReport, filters: SpendFilters, contractId: string): ContractDetail | null {
  const row = report.contracts.find((r) => r.contractId === contractId);
  if (!row) return null;
  const year = selectedYear(report, filters);
  const idx = yearIndexes(report, year);
  const spend = spendOf(row, idx, filters.include);
  const all = report.contracts.reduce((s, r) => s + spendOf(r, idx, filters.include), 0) || 1;
  const sameCategory = report.contracts.filter((r) => (r.category ?? "") === (row.category ?? "")).reduce((s, r) => s + spendOf(r, idx, filters.include), 0) || 1;
  const start = report.fiscalYears[0]?.from ?? report.today;
  const end = report.fiscalYears[report.fiscalYears.length - 1]?.to ?? report.today;
  const span = Math.max(1, daysBetween(start, end) + 1);
  const pos = (iso: string) => Math.max(0, Math.min(100, (daysBetween(start, iso) / span) * 100));
  const running = row.segments.find((s) => s.from <= report.today && report.today <= s.to) ?? null;
  const locked = row.segments.find((s) => s.kind === "rolledOver" && s.from > report.today) ?? null;
  return {
    row,
    share: spend / all,
    categoryShare: spend / sameCategory,
    spend,
    currentAnnual: currentAnnual(report, row),
    running: running ? { from: running.from, to: running.to } : null,
    lockedRenewal: locked ? { from: locked.from, to: locked.to, annualValue: locked.annualValue } : null,
    quarters: idx.map((i) => ({
      label: `Q${report.periods[i]!.quarter}`,
      actual: row.actual[i] ?? 0,
      committed: row.committed[i] ?? 0,
      autoRenewal: filters.include !== "committed" ? (row.autoRenewal[i] ?? 0) : 0,
      toDecide: filters.include === "all" ? (row.toDecide[i] ?? 0) : 0,
    })),
    timeline: row.segments
      .filter((s) => s.to >= start && s.from <= end)
      .map((s) => {
        const left = pos(s.from < start ? start : s.from);
        const right = pos(s.to > end ? end : s.to) + 100 / span;
        return { left, width: Math.max(0.4, right - left), kind: s.kind, from: s.from, to: s.to, annualValue: s.annualValue };
      }),
    ticks: report.fiscalYears.map((y) => ({ label: y.label, left: pos(y.from) })),
    todayLeft: pos(report.today),
    decideLeft: row.decision.decideBy ? pos(row.decision.decideBy) : null,
    savings: report.savings.filter((s) => s.contractId === contractId),
    objective: report.objectives.find((o) => o.contractId === contractId) ?? null,
  };
}

export interface BudgetAction {
  row: SpendContractRow;
  inPlay: number;
  renewsByItself: boolean;
  decisionDay: string | null;
  decisionDays: number | null;
  objective: SpendObjectiveEntry | null;
}

export interface Budget {
  year: number;
  label: string;
  committed: number;
  autoRenewal: number;
  toDecide: number;
  total: number;
  /** Against the selected year, everything included; null when that year has nothing. */
  changePct: number | null;
  actions: BudgetAction[];
}

/** The year after the selected one, as the contracts stand: committed, renewing by itself, to decide. */
export function budget(report: SpendReport, filters: SpendFilters): Budget | null {
  const year = selectedYear(report, filters) + 1;
  const fy = report.fiscalYears.find((y) => y.fiscalYear === year);
  if (!fy) return null;
  const rows = filteredRows(report, filters);
  const idx = yearIndexes(report, year);
  const prevIdx = yearIndexes(report, year - 1);
  const committed = rows.reduce((s, r) => s + sumAt(r.actual, idx) + sumAt(r.committed, idx), 0);
  const autoRenewal = rows.reduce((s, r) => s + sumAt(r.autoRenewal, idx), 0);
  const toDecide = rows.reduce((s, r) => s + sumAt(r.toDecide, idx), 0);
  const total = committed + autoRenewal + toDecide;
  const before = rows.reduce((s, r) => s + allSpendOf(r, prevIdx), 0);
  const actions = rows
    .map((r) => {
      const day = decisionDay(r);
      return {
        row: r,
        inPlay: sumAt(r.autoRenewal, idx) + sumAt(r.toDecide, idx),
        renewsByItself: r.decision.autoRenew,
        decisionDay: day,
        decisionDays: day ? daysBetween(report.today, day) : null,
        objective: report.objectives.find((o) => o.contractId === r.contractId) ?? null,
      };
    })
    .filter((a) => a.inPlay >= 0.5)
    .sort((a, b) => (a.decisionDays ?? Number.MAX_SAFE_INTEGER) - (b.decisionDays ?? Number.MAX_SAFE_INTEGER));
  return { year, label: fy.label, committed, autoRenewal, toDecide, total, changePct: before > 0 ? ((total - before) / before) * 100 : null, actions };
}

/** The table as CSV (comma-separated, quoted where needed, figures unrounded to the cent). */
export function pivotCsv(p: Pivot, currency: string, labels: { category: string; supplier: string; contract: string; share: string; savings: string; decideBy: string; noCategory: string }): string {
  const esc = (v: string) => (/[",\n;]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v);
  const num = (v: number) => (Math.round(v * 100) / 100).toFixed(2);
  const head = [labels.category, labels.supplier, labels.contract, ...p.columns.map((c) => `${c.label} (${currency})`), labels.share, `${labels.savings} (${currency})`, labels.decideBy];
  const lines = p.groups.flatMap((g) =>
    g.lines.map((l) => [g.category || labels.noCategory, l.supplier, l.name, ...l.values.map(num), (l.share * 100).toFixed(1) + "%", num(l.savings), l.decisionDay ?? ""]),
  );
  return [head, ...lines].map((r) => r.map(esc).join(",")).join("\n") + "\n";
}
