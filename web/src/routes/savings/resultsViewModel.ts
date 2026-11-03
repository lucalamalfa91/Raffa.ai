/**
 * Risultati (AskRaffa V4.dc.html `renderVals()` → `rs`, `gl`, `goalDraft`) as display values: the
 * server sends numbers (GET /api/savings/results, GET /api/savings/goals/active/progress) and
 * messages; this module turns them into the prototype's texts, bar heights and widths. Pure.
 * No estimate exists anywhere here: a figure is signed (contract), realized (invoices) or a target
 * the user set.
 */

import type { SavingsGoalProgressBody, SavingsResultsBody } from "../../api/client";
import type { Translate, Vars } from "../../i18n/translate";

/** A server message: `id` = English text, `vars` may nest messages. */
export interface ApiMsg {
  id: string;
  vars?: { [key: string]: unknown } | null;
}

export const tm = (t: Translate, m: ApiMsg | null | undefined): string => (m ? t(m.id, (m.vars ?? undefined) as Vars | undefined) : "");

/** `SQ.k(n)`: "CHF 128k", "CHF 2.4M". */
export function k(n: number): string {
  return n >= 1e6 ? `CHF ${(n / 1e6).toFixed(1)}M` : `CHF ${Math.round(n / 1000)}k`;
}

/** The prototype's `kk`: "42k". */
export function kk(n: number): string {
  return `${Math.round(n / 1000)}k`;
}

const MON = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** "2026-10-18" → "18 Oct 2026" (the prototype's data dates). */
export function day(iso: string | null | undefined): string {
  if (!iso) return "";
  const [y, m, d] = iso.split("-");
  return `${d} ${MON[Number(m) - 1]} ${y}`;
}

/** "Invoices Apr–Aug 2026 · 5 of 12 months" (the prototype's `realizedEv`, data in English). */
export function invoiceEvidence(proof: NonNullable<SavingsResultsBody["contractual"][number]["proof"]>): string {
  const [fy, fm] = proof.firstInvoice.split("-");
  const [ly, lm] = proof.lastInvoice.split("-");
  const from = MON[Number(fm) - 1];
  const to = MON[Number(lm) - 1];
  const range = fy === ly ? (fm === lm ? `${from} ${fy}` : `${from}–${to} ${ly}`) : `${from} ${fy}–${to} ${ly}`;
  const of = proof.periodMonths ? ` of ${proof.periodMonths}` : "";
  return `Invoices ${range} · ${proof.invoiceCount}${of} months`;
}

export const ACCENT = "var(--color-accent)";
export const ACCENT_700 = "var(--color-accent-700)";
export const INHERIT = "inherit";

export interface Figure {
  k: string;
  v: string;
  sub: string;
  fg: string;
}

export interface ResultsView {
  nums: Figure[];
  kpis: Figure[];
  months: { label: string; txt: string; h: string; bg: string; title: string }[];
  by: { label: string; wReal: string; wSig: string; wObj: string; txt: string }[];
  levers: { label: string; n: string; w: string; txt: string }[];
  proof: { label: string; w: string; txt: string }[];
  open: { key: string; supplier: string; est: string; status: string; tag: string; days: string; fg: string; caseId: string | null; contractId: string }[];
  recent: { key: string; when: string; supplier: string; note: string; baseline: string; period: string; method: string; evidence: string; amount: string; realized: string; realFg: string }[];
  volume: { key: string; when: string; supplier: string; note: string; amount: string }[];
  fiscalYear: number;
}

const LEVER: Record<string, string> = { price: "Unit price", term: "Term⟦lever⟧", clause: "Clauses", other: "Other⟦lever⟧" };

export function buildResultsView(r: SavingsResultsBody, t: Translate): ResultsView {
  const h = r.headline;
  const K = r.kpis;
  const nums: Figure[] = [
    {
      k: t("Contractual results · FY {year}", { year: r.fiscalYear }),
      v: k(h.contractual),
      sub: t("signed price vs previous contract, same quantities · {n} contracts", { n: h.count }),
      fg: ACCENT,
    },
    { k: t("Realized · from invoices"), v: k(h.realized), sub: t("a signed contract alone does not prove them"), fg: INHERIT },
    { k: t("Quantity reductions"), v: k(h.volume), sub: t("lower spend from cut quantities — shown apart"), fg: "var(--color-neutral-700)" },
  ];
  const next = K.nextNotice;
  const kpis: Figure[] = [
    { k: t("Signed results"), v: String(K.signedCount), sub: t("in the period, excluding quantity cuts"), fg: INHERIT },
    { k: t("Average per contract"), v: K.averagePerContract != null ? k(K.averagePerContract) : "—", sub: t("CHF / year"), fg: INHERIT },
    {
      k: t("Proven by invoices"),
      v: K.provenPercent != null ? `${K.provenPercent}%` : "—",
      sub: t("{realized} of {contractual}", { realized: k(K.realized), contractual: k(K.contractual) }),
      fg: ACCENT_700,
    },
    { k: t("Analysed spend"), v: k(K.analysedSpend), sub: t("{n} validated contracts", { n: K.validatedCount }), fg: INHERIT },
    {
      k: t("Open objectives"),
      v: k(K.openObjectives),
      sub: t("{n} renewals · {m} without objective", { n: K.openCount, m: K.withoutObjectiveCount }),
      fg: INHERIT,
    },
    {
      k: t("Next notice deadline"),
      v: next ? t("{n} d", { n: next.days }) : "—",
      sub: next ? `${next.supplier} · ${day(next.noticeDeadline)}` : "",
      fg: next && next.days <= 30 ? ACCENT : INHERIT,
    },
  ];

  const months = r.months.map((m) => ({
    label: m.label,
    txt: m.amount ? kk(m.amount) : "",
    h: `${(r.peak ? Math.max(m.amount ? 4 : 0, (m.amount / r.peak) * 96) : 0).toFixed(1)}%`,
    bg: m.amount ? ACCENT : "transparent",
    title: m.items.map((i) => `${i.supplier} ${k(i.amount)}`).join(", "),
  }));

  const sc = r.maxBySupplier || 1;
  const by = r.bySupplier.map((b) => {
    const rl = Math.min(b.realized, b.contractual);
    return {
      label: b.label,
      wReal: `${((rl / sc) * 100).toFixed(1)}%`,
      wSig: `${(((b.contractual - rl) / sc) * 100).toFixed(1)}%`,
      wObj: `${((b.objectives / sc) * 100).toFixed(1)}%`,
      txt: (b.contractual ? kk(b.contractual) : "") + (b.objectives ? (b.contractual ? " + " : "") + `${kk(b.objectives)} ob.` : ""),
    };
  });

  const lmax = Math.max(1, ...r.levers.map((l) => l.amount));
  const levers = r.levers.map((l) => ({
    label: t(LEVER[l.kind] ?? LEVER.other!),
    n: l.count === 1 ? t("1 contract") : t("{n} contracts", { n: l.count }),
    w: `${((l.amount / lmax) * 100).toFixed(1)}%`,
    txt: k(l.amount),
  }));

  const proof = r.proof.map((p) => ({
    label: p.supplier,
    w: p.amount ? `${Math.min(100, (p.realized / p.amount) * 100).toFixed(1)}%` : "0%",
    txt: p.realized > 0 ? `${kk(p.realized)} / ${kk(p.amount)}` : `0 / ${kk(p.amount)}`,
  }));

  const open = r.openObjectives.map((o) => ({
    key: o.contractId,
    supplier: o.supplier,
    est:
      o.objectiveKind == null
        ? t("No objective yet — set it in the case")
        : o.objectiveKind === "none"
          ? t("No target chosen")
          : o.objectivePercent && o.targetAmount != null
            ? t("Objective −{pct}% → CHF {amount}k / year if reached", { pct: o.objectivePercent, amount: Math.round(o.targetAmount / 1000) })
            : t("Custom objective"),
    status: o.hasCase ? t("In negotiation") : t("To evaluate"),
    tag: o.hasCase ? "tag-accent" : "tag-outline",
    days: !o.noticeDeadline ? t("No notice date") : o.days >= 0 ? t("{n} d to notice", { n: o.days }) : t("notice passed"),
    fg: o.noticeDeadline && o.days >= 0 && o.days <= 45 ? ACCENT : INHERIT,
    caseId: o.caseId ?? null,
    contractId: o.contractId,
  }));

  const recent = r.contractual.map((c) => ({
    key: c.id,
    when: day(c.signedOn),
    supplier: c.supplier,
    note: c.note,
    baseline: c.baseline,
    period: c.period,
    method: c.method,
    evidence: c.evidence,
    amount: t("{amount} / year", { amount: k(c.amount) }),
    realized: c.realized && c.proof
      ? t("Realized so far: {amount} · {evidence}", { amount: k(c.realizedAmount), evidence: invoiceEvidence(c.proof) })
      : t("Not yet proven by invoices"),
    realFg: c.realized ? "var(--color-text)" : "var(--color-neutral-600)",
  }));

  const volume = r.volume.map((v) => ({
    key: v.id,
    when: day(v.signedOn),
    supplier: v.supplier,
    note: v.note,
    amount: t("{amount} / year", { amount: k(v.amount) }),
  }));

  return { nums, kpis, months, by, levers, proof, open, recent, volume, fiscalYear: r.fiscalYear };
}

export interface GoalRowView {
  key: string;
  contractId: string;
  caseId: string | null;
  supplier: string;
  spend: string;
  line: string;
  ifHit: string;
  days: string;
  fg: string;
  cta: string;
  cls: string;
}

export interface GoalView {
  title: string;
  scope: string;
  pct: string;
  kpis: Figure[];
  wSigned: string;
  wObj: string;
  signedTxt: string;
  objTxt: string;
  gapTxt: string;
  verdict: string;
  rows: GoalRowView[];
  n: string;
  none: boolean;
}

export function buildGoalView(p: SavingsGoalProgressBody, t: Translate): GoalView {
  const g = p.goal;
  const label = tm(t, g.label as ApiMsg);
  const period = tm(t, g.periodLabel as ApiMsg);
  const pct = p.pct;
  const titleParts = [pct ? `${pct}%` : "", p.target ? t("{amount} a year", { amount: k(p.target) }) : "", period].filter(Boolean);
  const rows: GoalRowView[] = p.rows.map((r) => {
    const objective =
      r.objectiveKind == null
        ? t("to set")
        : r.objectiveKind === "none"
          ? t("none⟦objective⟧")
          : r.objectivePercent
            ? `−${r.objectivePercent}% (${k(r.objectiveAmount)})`
            : t("custom⟦objective⟧");
    const parts = [
      r.hasCase ? t("Case open · objective {objective}", { objective }) : t("Renewal to start"),
      t("uplift {value}", { value: r.uplift }),
      ...(r.autoRenewal ? [t("auto-renewal")] : []),
    ];
    return {
      key: r.contractId,
      contractId: r.contractId,
      caseId: r.caseId ?? null,
      supplier: r.supplier,
      spend: k(r.spend),
      line: parts.join(" · "),
      ifHit: t("{amount} / year", { amount: k(r.ifHit) }),
      days: !r.noticeDeadline ? "" : r.days >= 0 ? String(r.days) : "—",
      fg: r.noticeDeadline && r.days >= 0 && r.days <= 45 ? ACCENT : INHERIT,
      cta: r.hasCase ? t("Open the case") : t("Start the renewal"),
      cls: r.primary ? "btn-primary" : "btn-secondary",
    };
  });
  const verdict =
    p.verdict === "no_contracts"
      ? t("No validated contract in this scope.")
      : p.verdict === "covered"
        ? t("If the targets set in the cases are signed, the goal is covered. It counts only when the contract is signed.")
        : t(
            "To cover {gap} you need higher targets in the upcoming renewals: {pct}% on each contract below is worth {amount} a year. The target of each case is set at question 3 of the interview.",
            { gap: k(p.gap), pct, amount: k(p.ifAllHit) },
          );
  return {
    title: t("Towards your goal: {parts}", { parts: titleParts.join(" · ") }),
    scope: t("{label} · {spend} of annual spend on {n} contracts", { label, spend: k(p.spend), n: p.contractCount }),
    pct: `${pct}%`,
    kpis: [
      {
        k: t("Objective"),
        v: k(p.target),
        sub: pct ? t("{pct}% of {spend} · {label}", { pct, spend: k(p.spend), label }) : t("{spend} · {label}", { spend: k(p.spend), label }),
        fg: INHERIT,
      },
      {
        k: t("Signed so far"),
        v: k(p.signed),
        sub: p.signed
          ? t("{pct}% of the goal · signed price vs previous", { pct: p.target ? Math.round((p.signed / p.target) * 100) : 0 })
          : t("no contract signed in this scope"),
        fg: ACCENT,
      },
      { k: t("Objectives in the cases"), v: k(p.objectives), sub: t("targets you set in open renewals — not estimates"), fg: INHERIT },
      {
        k: t("Still to cover"),
        v: k(p.gap),
        sub: p.gap ? t("with the renewals coming up below") : t("covered, if the targets are reached"),
        fg: p.gap ? ACCENT_700 : INHERIT,
      },
    ],
    wSigned: `${p.signedWidth.toFixed(1)}%`,
    wObj: `${p.objectivesWidth.toFixed(1)}%`,
    signedTxt: k(p.signed),
    objTxt: k(p.objectives),
    gapTxt: p.gap ? t("missing {amount}", { amount: k(p.gap) }) : t("goal covered by the targets"),
    verdict,
    rows,
    n: String(rows.length),
    none: rows.length === 0,
  };
}

/** The example chips under the goal input (the prototype's `goalExamples`). */
export const GOAL_EXAMPLES = [
  "I want to save 20% on IT contracts next year",
  "CHF 300k on software contracts in 2026",
  "10% on cloud",
] as const;
