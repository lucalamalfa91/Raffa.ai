import { useEffect, useMemo, useRef, useState } from "react";
import { Link } from "react-router-dom";
import type { SpendReport } from "../../api/spend";
import { useI18n } from "../../i18n/I18nProvider";
import {
  budget,
  categories,
  cell,
  chartColumns,
  contractDetail,
  kpis,
  money,
  pivot,
  pivotCsv,
  selectedYear,
  type SpendFilters,
  type SpendInclude,
  type SpendSort,
} from "./spendViewModel";

type Change = (next: Partial<SpendFilters>) => void;
type T = (id: string, vars?: Record<string, string | number>) => string;

const enDate = (iso: string | null | undefined): string => {
  if (!iso) return "—";
  const [y, m, d] = iso.split("-");
  const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
  return `${d} ${months[Number(m) - 1] ?? ""} ${y}`;
};

const pct = (x: number, digits = 1) => `${(x * 100).toFixed(digits)}%`;

function billingLabel(t: T, period: string | null): string {
  switch (period) {
    case "annual":
      return t("Annual");
    case "semiannual":
      return t("Every six months");
    case "quarterly":
      return t("Quarterly");
    case "monthly":
      return t("Monthly");
    default:
      return t("not read");
  }
}

function Chips<V extends string | number>({ label, value, options, onPick }: { label: string; value: V; options: { value: V; label: string }[]; onPick: (v: V) => void }) {
  return (
    <div className="sp4-filter" role="group" aria-label={label}>
      <span className="sp4-filter-k">{label}</span>
      <div className="sp4-chips">
        {options.map((o) => (
          <button key={String(o.value)} type="button" className="sp4-chip" aria-pressed={o.value === value} onClick={() => onPick(o.value)}>
            {o.label}
          </button>
        ))}
      </div>
    </div>
  );
}

/** The filters: they apply to every section of the page and live in the URL. */
export function SpendFiltersBar({ report, filters, onChange }: { report: SpendReport; filters: SpendFilters; onChange: Change }) {
  const { t } = useI18n();
  const year = selectedYear(report, filters);
  const cats = categories(report);
  return (
    <div className="sp4-filters" role="group" aria-label={t("Filters for the whole page")}>
      <Chips label={t("View")} value={filters.view} onPick={(view) => onChange({ view })} options={[{ value: "year", label: t("Years") }, { value: "quarter", label: t("Quarters") }]} />
      <Chips label={t("Fiscal year")} value={year} onPick={(y) => onChange({ year: y })} options={report.fiscalYears.map((y) => ({ value: y.fiscalYear, label: y.label }))} />
      <Chips label={t("Spend basis")} value={filters.basis} onPick={(basis) => onChange({ basis })} options={[{ value: "accrual", label: t("Accrual") }, { value: "cash", label: t("Cash") }]} />
      <Chips<SpendInclude>
        label={t("Include")}
        value={filters.include}
        onPick={(include) => onChange({ include })}
        options={[
          { value: "committed", label: t("Committed only") },
          { value: "auto", label: t("+ Automatic renewals") },
          { value: "all", label: t("+ To decide") },
        ]}
      />
      <label className="sp4-filter">
        <span className="sp4-filter-k">{t("Category")}</span>
        <select className="input sp4-select" value={filters.category ?? "*"} onChange={(e) => onChange({ category: e.target.value === "*" ? null : e.target.value, contract: null })}>
          <option value="*">{t("All categories")}</option>
          {cats.map((c) => (
            <option key={c || "none"} value={c}>
              {c || t("No category")}
            </option>
          ))}
        </select>
      </label>
      <p className="sp4-note sp4-filters-note">
        {t("Amounts in {currency}, the reporting currency the admin chose; fiscal year from Settings.", { currency: report.currency })}{" "}
        {filters.basis === "accrual"
          ? t("Accrual: each contract's value is spread over the days it runs.")
          : t("Cash: each billing period's value on the day it is invoiced, read from the price lines.")}
      </p>
    </div>
  );
}

/** Contracts left out of the totals, and how to bring them in. */
export function ExcludedNote({ report }: { report: SpendReport }) {
  const { t } = useI18n();
  if (report.excluded.length === 0) return null;
  const noRate = report.excluded.filter((x) => x.reason === "noRate");
  const currencies = [...new Set(noRate.map((x) => x.currency))].sort();
  const count = (reason: string) => report.excluded.filter((x) => x.reason === reason).length;
  return (
    <div className="sp4-excluded" role="note">
      <strong>{t("{n} contracts are out of the totals, never estimated:", { n: report.excluded.length })}</strong>{" "}
      {noRate.length > 0 ? (
        <span>
          {t("{n} without a budget rate ({currencies})", { n: noRate.length, currencies: currencies.join(", ") })} —{" "}
          <Link to="/settings">{t("set the rates in Settings")}</Link>.{" "}
        </span>
      ) : null}
      {count("noDates") > 0 ? <span>{t("{n} without a start or end date.", { n: count("noDates") })} </span> : null}
      {count("noValue") > 0 ? <span>{t("{n} without an annual or total value.", { n: count("noValue") })} </span> : null}
      {count("noBillingPeriod") > 0 ? <span>{t("{n} without a billing period (cash basis).", { n: count("noBillingPeriod") })}</span> : null}
    </div>
  );
}

const KIND_CLASS = { actual: "sp4-k-actual", committed: "sp4-k-committed", autoRenewal: "sp4-k-auto", toDecide: "sp4-k-decide" } as const;

/** KPIs, then spend per period with the savings of the same periods underneath. */
export function SpendOverview({ report, filters, onChange }: { report: SpendReport; filters: SpendFilters; onChange: Change }) {
  const { t } = useI18n();
  const k = kpis(report, filters);
  const cols = chartColumns(report, filters);
  const max = Math.max(1, ...cols.map((c) => c.total));
  const smax = Math.max(1, ...cols.map((c) => c.proven + c.signedOnly + c.objective));
  const cur = report.currency;
  const tiles: { k: string; v: string; sub: string; strong?: boolean }[] = [
    {
      k: t("Spend {year} · {basis}", { year: k.yearLabel, basis: filters.basis === "accrual" ? t("accrual") : t("cash") }),
      v: money(k.spend, cur),
      sub: k.year === report.currentFiscalYear ? t("Actual to date {a} · rest {b}", { a: money(k.actualToDate, cur), b: money(k.spend - k.actualToDate, cur) }) : t("For the filters above"),
    },
    {
      k: report.fiscalYears.some((y) => y.fiscalYear === k.year - 1)
        ? t("Against {year}", { year: report.fiscalYears.find((y) => y.fiscalYear === k.year - 1)!.label })
        : t("Against the year before"),
      v: k.changePct === null ? "—" : `${k.changePct >= 0 ? "+" : ""}${k.changePct.toFixed(1)}%`,
      sub:
        k.changePct === null
          ? k.previous === null
            ? t("No earlier year in this report")
            : t("Not comparable: documented terms for {a} contracts in the earlier year, {b} in this one", { a: k.previousDocumented, b: k.documented })
          : t("{amount} in the earlier year", { amount: money(k.previous ?? 0, cur) }),
    },
    {
      k: t("Signed savings {year}", { year: k.yearLabel }),
      v: money(k.savingsSigned, cur),
      sub:
        k.quantityReduction >= 0.5
          ? t("proven by invoices {a} · quantity reductions {b} apart", { a: money(k.savingsProven, cur), b: money(k.quantityReduction, cur) })
          : t("proven by invoices {a}", { a: money(k.savingsProven, cur) }),
    },
    {
      k: k.nextLabel ? t("Committed {year}", { year: k.nextLabel }) : t("Committed next year"),
      v: k.nextLabel ? money(k.nextCommitted, cur) : "—",
      sub: k.nextLabel && k.nextTotal > 0 ? t("{p} of {total} if nothing changes", { p: pct(k.nextCommitted / k.nextTotal, 0), total: money(k.nextTotal, cur) }) : t("Outside this report"),
    },
    {
      k: t("To decide in the next 12 months"),
      v: money(k.decideSoon, cur),
      sub: k.firstDecision ? t("{n} contracts · first: {supplier}, {date}", { n: k.decideSoonCount, supplier: k.firstDecision.supplier, date: enDate(k.firstDecision.on) }) : t("No decision due"),
      strong: true,
    },
    {
      k: t("Documented in {year}", { year: k.yearLabel }),
      v: `${k.documented} / ${k.contracts}`,
      sub: k.documented < k.contracts ? t("contracts with a term a document states; the rest is not reconstructed") : t("every contract has a documented term in this year"),
    },
  ];
  return (
    <section id="overview" className="sp4-section" aria-labelledby="sp4-overview-h">
      <h3 id="sp4-overview-h" className="visually-hidden">
        {t("Overview")}
      </h3>
      <div className="sp4-kpis">
        {tiles.map((x) => (
          <div key={x.k} className="sp4-kpi">
            <div className="sp4-kpi-k">{x.k}</div>
            <div className={x.strong ? "sp4-kpi-v is-alert" : "sp4-kpi-v"}>{x.v}</div>
            <div className="sp4-kpi-sub">{x.sub}</div>
          </div>
        ))}
      </div>

      <div className="sp4-chart-head">
        <h3 className="sp4-h">{filters.view === "year" ? t("Spend and savings by fiscal year") : t("Spend and savings by quarter")}</h3>
        <div className="sp4-legend">
          <span><span className="sp4-sw sp4-k-actual" />{t("Actual")}</span>
          <span><span className="sp4-sw sp4-k-committed" />{t("Committed (signed, or renewed by itself)")}</span>
          {filters.include !== "committed" ? <span><span className="sp4-sw sp4-k-auto" />{t("Renews by itself unless notice is given")}</span> : null}
          {filters.include === "all" ? <span><span className="sp4-sw sp4-k-decide" />{t("Ends: to decide, at the current price")}</span> : null}
        </div>
      </div>
      <div className="sp4-chart-scroll">
        <div className="sp4-chart" role="group" aria-label={t("Spend by period")}>
          {cols.map((c) => (
            <button
              key={c.key}
              type="button"
              className={c.selected ? "sp4-col is-selected" : "sp4-col"}
              aria-label={t("{period}: spend {amount}", { period: c.label, amount: money(c.total, cur) })}
              onClick={() => onChange({ year: c.fiscalYear })}
            >
              <span className="sp4-col-total">{money(c.total, cur)}</span>
              {(["toDecide", "autoRenewal", "committed", "actual"] as const).map((kind) =>
                c[kind] >= 0.5 ? <span key={kind} className={`sp4-seg ${KIND_CLASS[kind]}`} style={{ height: Math.max(2, Math.round((c[kind] / max) * 180)) }} /> : null,
              )}
            </button>
          ))}
        </div>
        <div className="sp4-axis">
          {cols.map((c) => (
            <span key={c.key} className={c.selected ? "sp4-axis-l is-selected" : "sp4-axis-l"}>
              {c.label}
              {c.current ? ` · ${t("today")}` : ""}
            </span>
          ))}
        </div>
        <div className="sp4-chart-head sp4-savings-head">
          <span className="sp4-filter-k">{t("Savings in the period · always accrual")}</span>
          <div className="sp4-legend">
            <span><span className="sp4-sw sp4-s-proven" />{t("Proven by invoices")}</span>
            <span><span className="sp4-sw sp4-s-signed" />{t("Signed, not yet proven")}</span>
            <span><span className="sp4-sw sp4-s-objective" />{t("Objective in the cases")}</span>
          </div>
        </div>
        <div className="sp4-chart sp4-chart-savings" role="group" aria-label={t("Savings by period")}>
          {cols.map((c) => (
            <div key={c.key} className={c.selected ? "sp4-col is-selected" : "sp4-col"}>
              <span className="sp4-col-total">{c.proven + c.signedOnly >= 0.5 ? money(c.proven + c.signedOnly, cur) : c.objective >= 0.5 ? t("obj. {amount}", { amount: money(c.objective, cur) }) : "—"}</span>
              {c.objective >= 0.5 ? <span className="sp4-seg sp4-s-objective" style={{ height: Math.max(2, Math.round((c.objective / smax) * 56)) }} /> : null}
              {c.signedOnly >= 0.5 ? <span className="sp4-seg sp4-s-signed" style={{ height: Math.max(2, Math.round((c.signedOnly / smax) * 56)) }} /> : null}
              {c.proven >= 0.5 ? <span className="sp4-seg sp4-s-proven" style={{ height: Math.max(2, Math.round((c.proven / smax) * 56)) }} /> : null}
            </div>
          ))}
        </div>
      </div>
      <p className="sp4-note">{t("Click a column to bring the whole page to that year. Years without a documented term stay empty: Raffa does not rebuild missing data.")}</p>
    </section>
  );
}

const SORTS: { value: SpendSort; label: string }[] = [
  { value: "spend", label: "Spend" },
  { value: "savings", label: "Savings" },
  { value: "supplier", label: "Supplier" },
  { value: "decision", label: "Decide by" },
];

function decisionText(t: T, day: string | null, days: number | null): string {
  if (!day || days === null) return "—";
  return days < 0 ? t("{date} · passed", { date: enDate(day) }) : t("{date} · {n} d", { date: enDate(day), n: days });
}

/** Category → contract, the selected year's columns, incidence, savings, next decision; a contract's detail beside it. */
export function SpendPivot({
  report,
  filters,
  onChange,
  onOpenCase,
}: {
  report: SpendReport;
  filters: SpendFilters;
  onChange: Change;
  onOpenCase: (contractId: string, caseId: string | null) => void;
}) {
  const { t } = useI18n();
  const [closed, setClosed] = useState<Record<string, boolean>>({});
  const p = useMemo(() => pivot(report, filters), [report, filters]);
  const detail = filters.contract ? contractDetail(report, filters, filters.contract) : null;
  // When the detail opens below the table (narrow screens), bring it into view.
  const detailRef = useRef<HTMLElement | null>(null);
  useEffect(() => {
    if (filters.contract) detailRef.current?.scrollIntoView?.({ block: "nearest", behavior: "smooth" });
  }, [filters.contract]);
  const year = selectedYear(report, filters);
  const yearLabel = report.fiscalYears.find((y) => y.fiscalYear === year)?.label ?? String(year);
  const cur = report.currency;

  const exportCsv = () => {
    const csv = pivotCsv(p, cur, {
      category: t("Category"),
      supplier: t("Supplier"),
      contract: t("Contract"),
      share: t("Share"),
      savings: t("Savings"),
      decideBy: t("Decide by"),
      noCategory: t("No category"),
    });
    const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
    const a = document.createElement("a");
    a.href = url;
    a.download = `spend-${yearLabel.replace(/[^0-9A-Za-z]+/g, "-")}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <section id="detail" className="sp4-section" aria-labelledby="sp4-pivot-h">
      <div className="sp4-section-head">
        <h3 id="sp4-pivot-h" className="sp4-h">
          {filters.view === "year" ? t("Contracts and incidence · all years") : t("Contracts and incidence · {year} by quarter", { year: yearLabel })}
        </h3>
        <div className="sp4-section-tools">
          <Chips<SpendSort> label={t("Sort by")} value={filters.sort} onPick={(sort) => onChange({ sort })} options={SORTS.map((s) => ({ value: s.value, label: t(s.label) }))} />
          <button type="button" className="btn btn-secondary" onClick={exportCsv}>
            {t("Export CSV")}
          </button>
        </div>
      </div>
      <p className="sp4-note">{t("Open a category to see its contracts; click a contract for its incidence, terms and savings.")}</p>
      <div className="sp4-pivot-wrap">
        <div className="sp4-table-scroll">
          <table className="sp4-table">
            <thead>
              <tr>
                <th scope="col">{t("Category · contract")}</th>
                {p.columns.map((c) => (
                  <th key={c.key} scope="col" className={c.strong ? "sp4-num is-strong" : "sp4-num"}>
                    {c.label}
                  </th>
                ))}
                {p.deltaColumn ? <th scope="col" className="sp4-num">Δ</th> : null}
                <th scope="col" className="sp4-num">{t("Share")}</th>
                <th scope="col" className="sp4-num">{t("Savings {year}", { year: yearLabel })}</th>
                <th scope="col">{t("Decide by")}</th>
              </tr>
            </thead>
            {p.groups.map((g) => {
              const open = !closed[g.category];
              return (
                <tbody key={g.category || "none"}>
                  <tr className="sp4-group">
                    <th scope="rowgroup">
                      <button type="button" className="sp4-group-toggle" aria-expanded={open} onClick={() => setClosed((x) => ({ ...x, [g.category]: open }))}>
                        <span aria-hidden="true">{open ? "▾" : "▸"}</span> {g.category || t("No category")} <span className="sp4-muted">· {g.lines.length === 1 ? t("1 contract") : t("{n} contracts", { n: g.lines.length })}</span>
                      </button>
                    </th>
                    {g.values.map((v, i) => (
                      <td key={i} className="sp4-num is-strong">{cell(v)}</td>
                    ))}
                    {p.deltaColumn ? <td /> : null}
                    <td className="sp4-num is-strong">{pct(g.share)}</td>
                    <td className="sp4-num is-strong">{cell(g.savings)}</td>
                    <td />
                  </tr>
                  {open
                    ? g.lines.map((l) => (
                        <tr key={l.contractId} className={filters.contract === l.contractId ? "sp4-line is-selected" : "sp4-line"}>
                          <th scope="row">
                            <button
                              type="button"
                              className="sp4-line-pick"
                              aria-pressed={filters.contract === l.contractId}
                              onClick={() => onChange({ contract: filters.contract === l.contractId ? null : l.contractId })}
                            >
                              <span className="sp4-line-sup">{l.supplier}</span>
                              <span className="sp4-line-name">{l.name}</span>
                            </button>
                          </th>
                          {l.values.map((v, i) => (
                            <td key={i} className={p.columns[i]!.strong ? "sp4-num is-col" : "sp4-num"}>{cell(v)}</td>
                          ))}
                          {p.deltaColumn ? (
                            <td className="sp4-num">{l.delta === null ? "—" : l.delta === "new" ? t("new") : l.delta === "ended" ? t("ended") : `${l.delta >= 0 ? "+" : ""}${l.delta.toFixed(1)}%`}</td>
                          ) : null}
                          <td className="sp4-num">{pct(l.share)}</td>
                          <td className={l.savings >= 0.5 ? "sp4-num is-strong" : "sp4-num sp4-muted"}>{cell(l.savings)}</td>
                          <td className={l.decisionDays !== null && l.decisionDays >= 0 && l.decisionDays <= 90 ? "sp4-decide is-soon" : "sp4-decide"}>{decisionText(t, l.decisionDay, l.decisionDays)}</td>
                        </tr>
                      ))
                    : null}
                </tbody>
              );
            })}
            <tfoot>
              <tr>
                <th scope="row">{filters.category === null ? t("Total") : t("Total · {category}", { category: filters.category || t("No category") })}</th>
                {p.total.values.map((v, i) => (
                  <td key={i} className="sp4-num is-strong">{cell(v)}</td>
                ))}
                {p.deltaColumn ? <td /> : null}
                <td className="sp4-num is-strong">{pct(p.total.share)}</td>
                <td className="sp4-num is-strong">{cell(p.total.savings)}</td>
                <td />
              </tr>
            </tfoot>
          </table>
        </div>
        {detail ? (
          <aside ref={detailRef} className="sp4-detail" aria-label={t("Incidence and savings of the contract")}>
            <div className="sp4-detail-head">
              <div>
                <div className="sp4-kicker">{detail.row.category || t("No category")}</div>
                <h4 className="sp4-detail-title">{detail.row.supplier}</h4>
                <div className="sp4-muted">{detail.row.name}</div>
              </div>
              <button type="button" className="btn btn-ghost" aria-label={t("Close the detail")} onClick={() => onChange({ contract: null })}>
                ✕
              </button>
            </div>
            <div className="sp4-detail-share">
              <span className="sp4-detail-big">{pct(detail.share)}</span>
              <span className="sp4-muted">{t("of the {year} spend, every contract", { year: yearLabel })}</span>
              <span>{t("{amount} in {year} · {p} of {category}", { amount: money(detail.spend, cur), year: yearLabel, p: pct(detail.categoryShare), category: detail.row.category || t("No category") })}</span>
            </div>
            <dl className="sp4-facts">
              <dt>{t("Current annual value")}</dt>
              <dd>{detail.currentAnnual === null ? t("total value only") : money(detail.currentAnnual, cur)}</dd>
              <dt>{t("Running term")}</dt>
              <dd>{detail.running ? `${enDate(detail.running.from)} – ${enDate(detail.running.to)}` : "—"}</dd>
              <dt>{t("Renewal")}</dt>
              <dd>
                {detail.row.decision.autoRenew
                  ? detail.row.decision.renewalTermMonths
                    ? t("Automatic, {n} months{uplift}", { n: detail.row.decision.renewalTermMonths, uplift: detail.row.decision.uplift ? ` · ${t("increase up to {u}", { u: detail.row.decision.uplift })}` : "" })
                    : t("Automatic, term not read")
                  : t("Not automatic: to decide at the end")}
              </dd>
              {detail.lockedRenewal ? (
                <>
                  <dt>{t("Already renewed by itself")}</dt>
                  <dd>{t("{from} – {to}, notice passed, at the previous price", { from: enDate(detail.lockedRenewal.from), to: enDate(detail.lockedRenewal.to) })}</dd>
                </>
              ) : null}
              <dt>{t("Billing")}</dt>
              <dd>{billingLabel(t, detail.row.billingPeriod)}</dd>
              <dt>{detail.row.decision.decideBy ? t("Decide by") : t("Ends")}</dt>
              <dd>{enDate(detail.row.decision.decideBy ?? detail.row.decision.endsOn)}</dd>
              {detail.row.currency !== cur ? (
                <>
                  <dt>{t("Contract currency")}</dt>
                  <dd>{t("{code}, at the budget rate {rate}", { code: detail.row.currency, rate: detail.row.rate })}</dd>
                </>
              ) : null}
            </dl>
            <div className="sp4-filter-k">{t("Terms over time")}</div>
            <div className="sp4-timeline" role="img" aria-label={t("Terms of {supplier} in this report", { supplier: detail.row.supplier })}>
              {detail.timeline.map((b, i) => (
                <span
                  key={i}
                  className={`sp4-tl ${b.kind === "documented" ? "sp4-k-actual" : b.kind === "rolledOver" ? "sp4-k-committed" : b.kind === "autoRenewal" ? "sp4-k-auto" : "sp4-k-decide"}`}
                  style={{ left: `${b.left}%`, width: `${b.width}%` }}
                  title={`${enDate(b.from)} – ${enDate(b.to)}${b.annualValue !== null ? ` · ${money(b.annualValue, cur)} / ${t("year")}` : ""}`}
                />
              ))}
              <span className="sp4-tl-mark" style={{ left: `${detail.todayLeft}%` }} aria-hidden="true" />
              {detail.decideLeft !== null ? <span className="sp4-tl-mark is-decide" style={{ left: `${detail.decideLeft}%` }} aria-hidden="true" /> : null}
            </div>
            <div className="sp4-ticks" aria-hidden="true">
              {detail.ticks.map((x) => (
                <span key={x.label} style={{ left: `${x.left}%` }}>{x.label}</span>
              ))}
            </div>
            <div className="sp4-legend">
              <span><span className="sp4-sw sp4-k-actual" />{t("Documented")}</span>
              <span><span className="sp4-sw sp4-k-committed" />{t("Renewed by itself")}</span>
              <span><span className="sp4-sw sp4-k-auto" />{t("Renews by itself")}</span>
              <span><span className="sp4-sw sp4-k-decide" />{t("To decide")}</span>
            </div>
            <div className="sp4-filter-k">{t("By quarter · {year}", { year: yearLabel })}</div>
            <div className="sp4-quarters">
              {detail.quarters.map((q) => {
                const total = q.actual + q.committed + q.autoRenewal + q.toDecide;
                return (
                  <div key={q.label} className="sp4-quarter">
                    <span className="sp4-quarter-v">{cell(total)}</span>
                    <span className="sp4-quarter-l">{q.label}</span>
                  </div>
                );
              })}
            </div>
            <div className="sp4-filter-k">{t("Savings on this contract")}</div>
            {detail.savings.length === 0 && !detail.objective ? <p className="sp4-note">{t("No saving recorded on this contract.")}</p> : null}
            {detail.savings.map((s) => (
              <div key={s.id} className="sp4-saving">
                <div className="sp4-saving-head">
                  <strong>{s.countsAsNegotiation ? t("Contractual result · {note}", { note: s.note }) : t("Quantity reduction · apart")}</strong>
                  <span>{s.amountAnnualReporting === null ? `${s.currency} ${Math.round(s.amountAnnual / 1000)}k` : money(s.amountAnnualReporting, cur)} / {t("year")}</span>
                </div>
                <span className="sp4-muted">{[s.baseline, s.period, s.method, s.evidence].filter(Boolean).join(" · ")}</span>
                <span className="sp4-saving-state">
                  {s.excluded === "noRate"
                    ? t("Out of the totals: no budget rate for {code}", { code: s.currency })
                    : s.excluded === "noPeriod"
                      ? t("No period stated: in the register, not in time")
                      : s.provenTo
                        ? s.invoiceCount === 1
                          ? t("Proven by 1 invoice up to {date}", { date: enDate(s.provenTo) })
                          : t("Proven by {n} invoices up to {date}", { n: s.invoiceCount, date: enDate(s.provenTo) })
                        : t("Signed, not yet proven by an invoice")}
                </span>
              </div>
            ))}
            {detail.objective ? (
              <div className="sp4-saving">
                <div className="sp4-saving-head">
                  <strong>{t("Objective in the case")}</strong>
                  <span>{detail.objective.annualTarget === null ? t("not set") : `${money(detail.objective.annualTarget, cur)} / ${t("year")}`}</span>
                </div>
                <span className="sp4-saving-state is-objective">{t("Your target, not signed yet")}</span>
              </div>
            ) : null}
            <div className="sp4-detail-actions">
              <button type="button" className="btn btn-primary" onClick={() => onOpenCase(detail.row.contractId, detail.objective?.caseId ?? null)}>
                {detail.objective ? t("Open the case") : t("Start the renewal")}
              </button>
              <Link className="btn btn-secondary" to={`/contracts/${detail.row.contractId}`}>
                {t("Open the contract")}
              </Link>
            </div>
            <p className="sp4-note">{t("From the uploaded contract: terms, value, billing and renewal clause. Future years follow the clause, never a market estimate.")}</p>
          </aside>
        ) : null}
      </div>
    </section>
  );
}

/** The year after the selected one, and the contracts whose spend there still depends on a decision. */
export function SpendBudget({ report, filters, onChange, onOpenCase }: { report: SpendReport; filters: SpendFilters; onChange: Change; onOpenCase: (contractId: string, caseId: string | null) => void }) {
  const { t } = useI18n();
  const b = budget(report, filters);
  if (!b) return null;
  const cur = report.currency;
  const w = (v: number) => `${b.total > 0 ? (v / b.total) * 100 : 0}%`;
  return (
    <section id="budget" className="sp4-section" aria-labelledby="sp4-budget-h">
      <h3 id="sp4-budget-h" className="sp4-h">{t("Budget {year} from the contracts", { year: b.label })}</h3>
      <p className="sp4-lead">
        {t("For {year} you have already committed {committed} ({p}). {auto} renew by themselves unless you give notice in time; {decide} end and, without a decision, stay uncovered. If nothing changes the contracts cost {total}{change}.", {
          year: b.label,
          committed: money(b.committed, cur),
          p: b.total > 0 ? pct(b.committed / b.total, 0) : "0%",
          auto: money(b.autoRenewal, cur),
          decide: money(b.toDecide, cur),
          total: money(b.total, cur),
          change: b.changePct === null ? "" : ` (${b.changePct >= 0 ? "+" : ""}${b.changePct.toFixed(1)}%)`,
        })}
      </p>
      <div className="sp4-stack" role="img" aria-label={t("{year}: committed {a}, renewing by itself {b}, to decide {c}", { year: b.label, a: money(b.committed, cur), b: money(b.autoRenewal, cur), c: money(b.toDecide, cur) })}>
        <span className="sp4-k-committed" style={{ width: w(b.committed) }} />
        <span className="sp4-k-auto" style={{ width: w(b.autoRenewal) }} />
        <span className="sp4-k-decide" style={{ width: w(b.toDecide) }} />
      </div>
      <h4 className="sp4-h4">{t("Contracts you can act on")}</h4>
      <p className="sp4-note">{t("The {year} spend that depends on a decision of yours, by deadline, with the case objective when there is one. Raffa does not estimate a saving: it tells you what is at stake and until when you can act.", { year: b.label })}</p>
      <div className="sp4-table-scroll">
        <table className="sp4-table sp4-actions">
          <thead>
            <tr>
              <th scope="col">{t("Supplier · contract")}</th>
              <th scope="col" className="sp4-num">{t("At stake in {year}", { year: b.label })}</th>
              <th scope="col">{t("If you do nothing")}</th>
              <th scope="col">{t("Objective in the case")}</th>
              <th scope="col">{t("Decide by")}</th>
              <th scope="col"><span className="visually-hidden">{t("Action")}</span></th>
            </tr>
          </thead>
          <tbody>
            {b.actions.map((a) => (
              <tr key={a.row.contractId} className="sp4-line">
                <th scope="row">
                  <button type="button" className="sp4-line-pick" onClick={() => onChange({ contract: a.row.contractId })}>
                    <span className="sp4-line-sup">{a.row.supplier}</span>
                    <span className="sp4-line-name">{a.row.name}</span>
                  </button>
                </th>
                <td className="sp4-num is-strong">{money(a.inPlay, cur)}</td>
                <td>
                  {a.renewsByItself
                    ? a.row.decision.renewalTermMonths
                      ? t("Renews by itself for {n} months{uplift}", { n: a.row.decision.renewalTermMonths, uplift: a.row.decision.uplift ? `, ${t("increase up to {u}", { u: a.row.decision.uplift })}` : "" })
                      : t("Renews by itself (term not read)")
                    : t("Ends on {date}: without a decision you are left without a contract", { date: enDate(a.row.decision.endsOn) })}
                </td>
                <td className={a.objective?.annualTarget ? "sp4-objective" : "sp4-muted"}>
                  {a.objective?.annualTarget ? `${money(a.objective.annualTarget, cur)} / ${t("year")}` : t("No objective")}
                </td>
                <td className={a.decisionDays !== null && a.decisionDays >= 0 && a.decisionDays <= 90 ? "sp4-decide is-soon" : "sp4-decide"}>{decisionText(t, a.decisionDay, a.decisionDays)}</td>
                <td>
                  <button type="button" className={a.objective ? "btn btn-secondary" : "btn btn-primary"} onClick={() => onOpenCase(a.row.contractId, a.objective?.caseId ?? null)}>
                    {a.objective ? t("Open the case") : t("Start and set an objective")}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {b.actions.length === 0 ? <p className="sp4-note">{t("Nothing in {year} depends on a decision yet.", { year: b.label })}</p> : null}
    </section>
  );
}

/** The register: every recorded result with its base, period, method and evidence, and the cases' objectives. */
export function SpendLedger({ report, filters }: { report: SpendReport; filters: SpendFilters }) {
  const { t } = useI18n();
  const cur = report.currency;
  const inCategory = (contractId: string | null) =>
    filters.category === null || report.contracts.some((r) => r.contractId === contractId && (r.category ?? "") === filters.category);
  const contractual = report.savings.filter((s) => s.countsAsNegotiation && inCategory(s.contractId));
  const quantity = report.savings.filter((s) => !s.countsAsNegotiation && inCategory(s.contractId));
  const objectives = report.objectives.filter((o) => o.annualTarget !== null && inCategory(o.contractId));
  const amount = (value: number | null, code: string, original: number) => (value === null ? `${code} ${Math.round(original / 1000)}k` : money(value, cur));
  return (
    <section id="savings" className="sp4-section" aria-labelledby="sp4-ledger-h">
      <h3 id="sp4-ledger-h" className="sp4-h">{t("Recorded savings · each with base, period, method and evidence")}</h3>
      <div className="sp4-ledger">
        <div className="sp4-ledger-main">
          <h4 className="sp4-h4">{t("Contractual results · {n}", { n: contractual.length })}</h4>
          {contractual.length === 0 ? <p className="sp4-note">{t("No contractual result yet: one counts when a renewal is signed at a better price.")}</p> : null}
          {contractual.map((s) => (
            <div key={s.id} className="sp4-entry">
              <span className="sp4-entry-t">{s.supplier} · {s.note}</span>
              <span className="sp4-entry-v">{amount(s.amountAnnualReporting, s.currency, s.amountAnnual)} / {t("year")}</span>
              <span className="sp4-muted">{[enDate(s.signedOn), s.baseline, s.period, s.method, s.evidence].filter(Boolean).join(" · ")}</span>
              <span className="sp4-entry-state">
                {s.excluded === "noRate"
                  ? t("No budget rate for {code}: out of the totals", { code: s.currency })
                  : s.provenTo
                    ? t("Proven by invoices up to {date}", { date: enDate(s.provenTo) })
                    : t("Signed, not yet proven")}
              </span>
            </div>
          ))}
        </div>
        <div className="sp4-ledger-side">
          <h4 className="sp4-h4">{t("Quantity reductions · apart")}</h4>
          {quantity.length === 0 ? <p className="sp4-note">{t("None.")}</p> : null}
          {quantity.map((s) => (
            <div key={s.id} className="sp4-entry">
              <span className="sp4-entry-t">{s.supplier} · {s.note}</span>
              <span className="sp4-entry-v">{amount(s.amountAnnualReporting, s.currency, s.amountAnnual)} / {t("year")}</span>
              <span className="sp4-muted">{t("Spend no longer paid because quantities were cut, not a negotiation result")}</span>
            </div>
          ))}
          <h4 className="sp4-h4">{t("Open objectives in the cases")}</h4>
          {objectives.length === 0 ? <p className="sp4-note">{t("No case has a target yet.")}</p> : null}
          {objectives.map((o) => (
            <div key={o.caseId} className="sp4-entry">
              <span className="sp4-entry-t">{o.supplier}</span>
              <span className="sp4-entry-v">{money(o.annualTarget ?? 0, cur)} / {t("year")}</span>
              <span className="sp4-muted">
                {t("from {date}", { date: enDate(o.from) })}
                {o.owner ? ` · ${o.owner}` : ""}
                {o.assumedTermMonths ? ` · ${t("12 months: the renewal term was not read")}` : ""}
              </span>
            </div>
          ))}
        </div>
      </div>
      <p className="sp4-note">{t("A saving counts only with the signed contract (the signed price against the previous contract, same quantities) or with invoices. Quantity reductions are spend no longer paid, not a negotiation result: they stay apart and are not part of the goal. The cases' objectives are yours, not signed yet.")}</p>
    </section>
  );
}

/** In-page links to the sections. */
export function SpendSectionNav({ report, filters, hasGoal }: { report: SpendReport; filters: SpendFilters; hasGoal: boolean }) {
  const { t } = useI18n();
  const next = report.fiscalYears.find((y) => y.fiscalYear === selectedYear(report, filters) + 1);
  return (
    <nav className="sp4-sections" aria-label={t("Sections of the page")}>
      <a href="#overview">{t("Overview")}</a>
      {hasGoal ? <a href="#goal">{t("Towards your goal")}</a> : null}
      <a href="#detail">{t("Contracts and incidence")}</a>
      {next ? <a href="#budget">{t("Budget {year}", { year: next.label })}</a> : null}
      <a href="#savings">{t("Recorded savings")}</a>
    </nav>
  );
}

