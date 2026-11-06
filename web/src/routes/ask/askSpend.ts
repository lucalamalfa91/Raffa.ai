import type { SpendReport } from "../../api/spend";
import {
  budget,
  categories,
  chartColumns,
  DEFAULT_FILTERS,
  kpis,
  money,
  pivot,
  selectedYear,
  writeFilters,
  type SpendFilters,
  type SpendInclude,
} from "../savings/spendViewModel";
import type { Artifact, ArtifactKind, BarChart, DataTable, Tile } from "./artifacts";

type T = (id: string, vars?: Record<string, string | number>) => string;

/** What a request in the chat asks of the figures ("i KPI per anno", "la dashboard per trimestre, per cassa, 2027"). */
export interface SpendIntent {
  view: "year" | "quarter";
  basis: "accrual" | "cash";
  include: SpendInclude;
  /** A fiscal year named in the text ("2027", "FY 2027", "anno fiscale 2027"). */
  year: number | null;
  /** The request, to match a category of the report against. */
  text: string;
  /** The earlier requests for the same artifact, for a category this one does not name. */
  earlier: string;
}

const QUARTER = /\b(trimestr\w*|quarter\w*|q[1-4])\b/;
const YEARLY = /\b(per\s+anno|annual\w*|annuale|by\s+year|yearly|per\s+year)\b/;
const CASH = /\b(cassa|cash|uscite|pagamenti|payments?|fatturat\w*|invoiced)\b/;
const ACCRUAL = /\b(competenza|accrual)\b/;
const COMMITTED = /\b(solo\s+impegnat\w*|gi[aà]\s+impegnat\w*|committed\s+only|only\s+committed)\b/;
const AUTO = /\b(con\s+i\s+rinnovi\s+automatici|with\s+automatic\s+renewals)\b/;
const EVERYTHING = /\b(tutti\s+i\s+contratti|anche\s+(quelli\s+)?da\s+decidere|all\s+contracts|including\s+(those\s+)?to\s+decide)\b/;
const YEAR = /\b(?:fy\s*|af\s*|anno\s+(?:fiscale\s+)?|fiscal\s+year\s+)?(20\d{2})\b/;

/**
 * Reads the modifiers of a request. What it does not name stays as the earlier version had it
 * ("fallo per trimestre", then "solo impegnato": still by quarter), or the page's default.
 */
export function spendIntent(text: string, base: SpendIntent | null = null): SpendIntent {
  const s = text.toLowerCase();
  const year = YEAR.exec(s);
  return {
    view: QUARTER.test(s) ? "quarter" : YEARLY.test(s) ? "year" : (base?.view ?? "year"),
    basis: CASH.test(s) ? "cash" : ACCRUAL.test(s) ? "accrual" : (base?.basis ?? "accrual"),
    include: COMMITTED.test(s) ? "committed" : AUTO.test(s) ? "auto" : EVERYTHING.test(s) ? "all" : (base?.include ?? "all"),
    year: year ? Number(year[1]) : (base?.year ?? null),
    text: s,
    earlier: base ? `${base.earlier} ${base.text}`.trim() : "",
  };
}

/** The intent of the next version: every earlier request for the same artifact, in order, then this one. */
export function revisedIntent(earlier: readonly (string | null)[], text: string): SpendIntent {
  return spendIntent(text, earlier.reduce<SpendIntent | null>((base, r) => spendIntent(r ?? "", base), null));
}

/** The page's filters for an intent: a named year and category only when the report has them. */
export function intentFilters(report: SpendReport, intent: SpendIntent): SpendFilters {
  const named = (text: string) => categories(report).find((c) => c !== "" && text.includes(c.toLowerCase())) ?? null;
  const category = named(intent.text) ?? named(intent.earlier);
  return {
    ...DEFAULT_FILTERS,
    view: intent.view,
    basis: intent.basis,
    include: intent.include,
    year: intent.year !== null && report.fiscalYears.some((y) => y.fiscalYear === intent.year) ? intent.year : null,
    category,
  };
}

/** "/savings?view=quarter&year=2027": the page on the same filters. */
export function spendLink(filters: SpendFilters): string {
  const query = writeFilters({ ...filters, contract: null }).toString();
  return query ? `/savings?${query}` : "/savings";
}

/** "Not the whole workspace: 33 contracts (EUR, GBP, USD) are out of the totals, never estimated." */
function exclusionNote(report: SpendReport, t: T): string {
  const currencies = [...new Set(report.excluded.map((x) => x.currency).filter((c) => c))].sort();
  return t("Not the whole workspace: {n} contracts{currencies} are out of the totals, never estimated", {
    n: report.excluded.length,
    currencies: currencies.length ? ` (${currencies.join(", ")})` : "",
  });
}

/**
 * A KPI view, a dashboard or a portfolio brief built from Spesa e risparmi's own figures (the same
 * view model as the page, so the numbers are the page's): in the reporting currency, on the
 * workspace's fiscal year, by year or by quarter as the request asks, with the page one click away.
 */
export function buildSpendArtifact(kind: ArtifactKind, report: SpendReport, intent: SpendIntent, t: T, version = 1): Artifact {
  const filters = intentFilters(report, intent);
  const k = kpis(report, filters);
  const year = selectedYear(report, filters);
  const cur = report.currency;
  const basis = filters.basis === "accrual" ? t("accrual") : t("cash");
  const previous = report.fiscalYears.find((y) => y.fiscalYear === year - 1);

  const tiles: Tile[] = [
    { label: t("Spend {year} · {basis}", { year: k.yearLabel, basis }), value: money(k.spend, cur) },
    {
      label: previous ? t("Against {year}", { year: previous.label }) : t("Against the year before"),
      value: k.changePct === null ? "—" : `${k.changePct >= 0 ? "+" : ""}${k.changePct.toFixed(1)}%`,
      hint: k.changePct === null && previous ? t("Not comparable: documented terms for {a} contracts in the earlier year, {b} in this one", { a: k.previousDocumented, b: k.documented }) : undefined,
    },
    { label: t("Signed savings {year}", { year: k.yearLabel }), value: money(k.savingsSigned, cur), hint: t("proven by invoices {a}", { a: money(k.savingsProven, cur) }), tone: k.savingsSigned >= 0.5 ? "good" : undefined },
    { label: k.nextLabel ? t("Committed {year}", { year: k.nextLabel }) : t("Committed next year"), value: k.nextLabel ? money(k.nextCommitted, cur) : "—" },
    {
      label: t("To decide in the next 12 months"),
      value: money(k.decideSoon, cur),
      hint: k.firstDecision ? t("{n} contracts · first: {supplier}, {date}", { n: k.decideSoonCount, supplier: k.firstDecision.supplier, date: k.firstDecision.on }) : undefined,
      tone: k.decideSoon >= 0.5 ? "urgent" : undefined,
    },
    { label: t("Documented in {year}", { year: k.yearLabel }), value: `${k.documented} / ${k.contracts}` },
  ];

  const columns = chartColumns(report, filters).filter((c) => filters.view === "year" || c.fiscalYear === year);
  const trend: DataTable = {
    title: filters.view === "year" ? t("Spend and savings by fiscal year") : t("Spend and savings by quarter"),
    columns: [filters.view === "year" ? t("Fiscal year") : t("Quarter"), t("Spend"), t("Committed (signed, or renewed by itself)"), t("Savings"), filters.view === "year" ? t("Documented") : t("Objective in the cases")],
    rows: columns.map((c) => {
      const fy = report.fiscalYears.find((y) => y.fiscalYear === c.fiscalYear);
      return [
        c.label,
        money(c.total, cur),
        money(c.actual + c.committed, cur),
        money(c.proven + c.signedOnly, cur),
        filters.view === "year" ? (fy ? `${fy.documented} / ${fy.contracts}` : "—") : money(c.objective, cur),
      ];
    }),
  };

  const p = pivot(report, { ...filters, view: "year", sort: "spend" });
  const lines = p.groups.flatMap((g) => g.lines.map((l) => ({ ...l, category: g.category })));
  const b = budget(report, filters);
  const charts: BarChart[] = [
    { title: trend.title, items: columns.map((c) => ({ label: c.label, value: c.total, display: money(c.total, cur) })) },
    {
      title: t("Spend by category · {year}", { year: k.yearLabel }),
      items: p.groups.map((g) => ({ label: g.category || t("No category"), value: g.spend, display: money(g.spend, cur) })),
    },
  ];
  const decisions: DataTable = {
    title: t("Next decisions"),
    columns: [t("Supplier"), t("Contract"), t("Decide by"), t("Spend {year} · {basis}", { year: k.yearLabel, basis })],
    rows: [...lines]
      .filter((l) => l.decisionDay !== null && (l.decisionDays ?? -1) >= 0)
      .sort((a, z) => (a.decisionDays ?? 0) - (z.decisionDays ?? 0))
      .slice(0, 10)
      .map((l) => [l.supplier, l.name, l.decisionDay ?? "—", money(l.spend, cur)]),
  };

  const md: string[] = [];
  md.push(`**${t("Where we stand")}**`);
  md.push(t("In {year} the contracts cost {spend} ({basis}); {committed} of {next} is already committed for {nextYear}.", {
    year: k.yearLabel,
    spend: money(k.spend, cur),
    basis,
    committed: money(k.nextCommitted, cur),
    next: money(k.nextTotal, cur),
    nextYear: k.nextLabel ?? "—",
  }));
  md.push(t("{amount} depend on a decision in the next 12 months.", { amount: money(k.decideSoon, cur) }));
  md.push("");
  md.push(`**${t("To decide soon")}**`);
  for (const a of (b?.actions ?? []).slice(0, 6)) md.push(`- ${a.row.supplier} · ${a.decisionDay ?? "—"} · ${money(a.inPlay, cur)}`);
  if (!b || b.actions.length === 0) md.push(`- ${t("Nothing depends on a decision yet.")}`);
  md.push("");
  md.push(`**${t("Biggest contracts")}**`);
  for (const l of lines.sort((a, z) => z.spend - a.spend).slice(0, 5)) md.push(`- ${l.supplier} · ${money(l.spend, cur)}`);
  if (report.excluded.length) {
    md.push("");
    md.push(t("{n} contracts are out of the totals, never estimated:", { n: report.excluded.length }) + " " + report.excluded.map((x) => x.supplier).join(", "));
  }

  const view = filters.view === "year" ? t("by year") : t("by quarter");
  const title =
    kind === "kpi" ? `${t("Contract KPIs")} · ${view}` : kind === "dashboard" ? `${t("Contracts dashboard")} · ${view}` : t("Portfolio brief");
  return {
    id: `${kind}-${Date.now()}`,
    kind,
    title: filters.category !== null ? `${title} · ${filters.category || t("No category")}` : title,
    subtitle: [
      t("From Spend and savings · {year} · {basis} · in {currency}", { year: k.yearLabel, basis, currency: cur }),
      // The scope limit travels with every kind (and so with its exports): these are not the totals of the whole workspace.
      ...(report.excluded.length ? [exclusionNote(report, t)] : []),
    ].join(" · "),
    tiles: kind === "document" ? [] : tiles,
    charts: kind === "dashboard" ? charts : [],
    tables: kind === "kpi" ? [trend] : kind === "dashboard" ? [trend, decisions] : [],
    markdown: kind === "document" ? md.join("\n") : "",
    version,
    link: { label: t("Open in Spend and savings"), to: spendLink(filters) },
  };
}
