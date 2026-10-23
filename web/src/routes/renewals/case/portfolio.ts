/**
 * The renewals portfolio and the action queue, derived from the cases (pure).
 *
 * Renewals and Today are two views of the same thing: every contract is either not started or has a
 * case in some wizard step, and every case raises zero or more things somebody must do. Nothing here
 * reads a store or the DOM; the screens pass the cases in.
 */

import { daysBetween, isAfter, type CaseDate } from "./dates";
import { shortStatus, stage, todo } from "./engine";
import { DEMO_APPROVER, DEMO_CONTRACTS, DEMO_OWNER, demoContext } from "./fixtures";
import { STEPS } from "./model";
import { resolveText, translator, type Lang, type Translate } from "../../../i18n/translate";
import type { Case, CaseContext, TodoItem, TodoKind } from "./types";

export type Scope = "mine" | "team" | "unassigned";
export type KpiKey = "n30" | "n90" | "open" | "neg" | "closed";
export type RenewalsView = "cases" | "pipeline" | "queue";

/** One contract as the Renewals list sees it. */
export interface RenewalSummary {
  cid: string;
  supplier: string;
  /** "Sales Cloud Enterprise" -- the contract's short name. */
  short: string;
  fullName: string;
  hasCase: boolean;
  /** The case owner, or "No owner" before a case exists. */
  owner: string;
  /** Wizard step 0–6, or -1 when not started. */
  stage: number;
  status: string;
  done: boolean;
  /** The viewer owns the case or something is waiting on them. */
  mine: boolean;
  /** Days to the notice deadline. */
  noticeDays: number;
  /** Days to the end of the term. */
  endsInDays: number;
  cancel: CaseDate;
  end: CaseDate;
  autoRenews: boolean;
  uplift: string;
  rationale: string;
}

export const demoContractIds = (): string[] => Object.keys(DEMO_CONTRACTS);

export const shortName = (fullName: string): string => fullName.split(" — ")[0] ?? fullName;

export function contextFor(cid: string, viewer: string, lang: Lang): CaseContext {
  return demoContext(cid, viewer, lang);
}

function summarize(cid: string, c: Case | undefined, ctx: CaseContext, mineQueued: boolean, t: Translate): RenewalSummary {
  const C = ctx.contract;
  return {
    cid,
    supplier: C.supplier,
    short: shortName(C.name),
    fullName: C.name,
    hasCase: !!c,
    owner: c ? c.owner : t("No owner"),
    stage: c ? stage(c) : -1,
    status: c ? shortStatus(c, ctx.lang) : t("Not started"),
    done: !!c?.outcome,
    mine: mineQueued || (!!c && c.owner === ctx.viewer),
    noticeDays: daysBetween(ctx.today, C.cancel),
    endsInDays: daysBetween(ctx.today, C.end),
    cancel: C.cancel,
    end: C.end,
    autoRenews: C.autoRenews,
    uplift: C.uplift,
    rationale: C.rationale,
  };
}

/** Every contract with its case (if any), nearest notice deadline first. */
export function buildSummaries(cases: Readonly<Record<string, Case>>, viewer: string, lang: Lang): RenewalSummary[] {
  const t = translator(lang);
  const queue = buildQueue(cases, viewer, lang);
  return demoContractIds()
    .map((cid) => summarize(cid, cases[cid], contextFor(cid, viewer, lang), queue.some((item) => item.cid === cid && item.mine), t))
    .sort((a, b) => a.noticeDays - b.noticeDays);
}

export function inScope(list: readonly RenewalSummary[], scope: Scope): RenewalSummary[] {
  return list.filter((x) => (scope === "mine" ? x.mine : scope === "unassigned" ? !x.hasCase : true));
}

/* ---------- KPI strip: each cell is a filter ---------- */

export const KPI_FILTERS: Record<KpiKey, (x: RenewalSummary) => boolean> = {
  n30: (x) => !x.done && x.noticeDays <= 30,
  n90: (x) => !x.done && x.noticeDays <= 90,
  open: (x) => !x.hasCase,
  neg: (x) => x.hasCase && !x.done,
  closed: (x) => x.done,
};

export interface KpiCell {
  key: KpiKey;
  label: string;
  /** What the number means, as a tooltip. */
  meta: string;
  value: number;
  /** Red when above zero. */
  urgent: boolean;
}

export function buildKpis(base: readonly RenewalSummary[], t: Translate): KpiCell[] {
  const defs: Array<[KpiKey, string, string, boolean]> = [
    ["n30", t("Notice in 30 days"), t("act now"), true],
    ["n90", t("Notice in 90 days"), t("plan ahead"), false],
    ["open", t("Not started"), t("no owner yet"), false],
    ["neg", t("In negotiation"), t("open cases"), false],
    ["closed", t("Closed"), t("this cycle"), false],
  ];
  return defs.map(([key, label, meta, urgent]) => {
    const value = base.filter(KPI_FILTERS[key]).length;
    return { key, label, meta, value, urgent: urgent && value > 0 };
  });
}

export function applyKpi(list: readonly RenewalSummary[], kpi: KpiKey | null): RenewalSummary[] {
  return kpi ? list.filter(KPI_FILTERS[kpi]) : [...list];
}

/** "3 renewals open · 1 not started · sorted by notice deadline" */
export function headline(all: readonly RenewalSummary[], t: Translate): string {
  const open = all.filter((x) => x.hasCase).length;
  const notStarted = all.length - open;
  return `${t(open === 1 ? "{n} renewal open" : "{n} renewals open", { n: open })} · ${t("{n} not started", { n: notStarted })} · ${t("sorted by notice deadline")}`;
}

/** The seven mini bars of a card: done = ink, current = accent, nothing before the case starts. */
export type MiniBar = "done" | "now" | "todo";
export function miniBars(x: Pick<RenewalSummary, "stage" | "done">): MiniBar[] {
  return STEPS.map((_, i) => {
    if (x.stage < 0) return "todo";
    if (x.done || i < x.stage) return "done";
    return i === x.stage ? "now" : "todo";
  });
}

/** Deadline within 45 days: accent-700 and weight 600 (design-system semantic mapping). */
export const isNoticeUrgent = (x: Pick<RenewalSummary, "noticeDays" | "done">): boolean => !x.done && x.noticeDays <= 45;

/* ---------- action queue ---------- */

export interface QueueItem extends TodoItem {
  cid: string;
  /** Stable id of this item, for snooze / done. */
  key: string;
  supplier: string;
  contract: string;
  /** The case owner ("" before a case exists). */
  owner: string;
  hasCase: boolean;
  /** One line: "Step 3 / 7 · Negotiation", or "Not started". */
  phase: string;
  /** The viewer is the person it waits on. */
  mine: boolean;
}

export interface QueueState {
  snoozed: Readonly<Record<string, CaseDate>>;
  dismissed: Readonly<Record<string, true>>;
}
export const EMPTY_QUEUE: QueueState = { snoozed: {}, dismissed: {} };

const phaseLine = (stageIndex: number, lang: Lang, t: Translate): string =>
  stageIndex >= 0 ? `${t("Step {n} / 7", { n: stageIndex + 1 })} · ${resolveText(STEPS[stageIndex], lang)}` : t("Not started");

/** A contract with no case, close to its deadline, is an item for the approver (CPO) to start. */
function unassignedItem(cid: string, ctx: CaseContext, t: Translate): QueueItem {
  const C = ctx.contract;
  return {
    kind: "unassigned",
    who: DEMO_APPROVER,
    title: t("Notice deadline without an owner"),
    why: t(C.autoRenews ? "Auto-renews for 12 months if nothing happens by {date}. Start the renewal and assign it." : "Ends if nothing happens by {date}. Start the renewal and assign it.", { date: C.cancel }),
    due: C.cancel,
    dueDays: daysBetween(ctx.today, C.cancel),
    stage: -1,
    stageLabel: t("Not started"),
    ref: "",
    stalled: 0,
    cid,
    key: `${cid}:unassigned`,
    supplier: C.supplier,
    contract: shortName(C.name),
    owner: "",
    hasCase: false,
    phase: t("Not started"),
    mine: ctx.viewer === DEMO_APPROVER,
  };
}

/** Everything anyone must do, nearest deadline first (not yet filtered by snooze or done). */
export function buildQueue(cases: Readonly<Record<string, Case>>, viewer: string, lang: Lang): QueueItem[] {
  const t = translator(lang);
  const out: QueueItem[] = [];
  for (const cid of demoContractIds()) {
    const ctx = contextFor(cid, viewer, lang);
    const c = cases[cid];
    if (c) {
      for (const item of todo(c, ctx)) {
        out.push({
          ...item,
          cid,
          key: `${cid}:${item.kind}:${item.title}`,
          supplier: ctx.contract.supplier,
          contract: shortName(ctx.contract.name),
          owner: c.owner,
          hasCase: true,
          phase: phaseLine(item.stage, lang, t),
          mine: item.who === viewer,
        });
      }
    } else if (daysBetween(ctx.today, ctx.contract.cancel) <= 120) {
      out.push(unassignedItem(cid, ctx, t));
    }
  }
  return out.sort((a, b) => a.dueDays - b.dueDays);
}

/** Not done and not snoozed past today. */
export function visibleQueue(items: readonly QueueItem[], queue: QueueState, today: CaseDate): QueueItem[] {
  return items.filter((x) => {
    if (queue.dismissed[x.key]) return false;
    const until = queue.snoozed[x.key];
    return !until || !isAfter(until, today);
  });
}

export function snoozedItems(items: readonly QueueItem[], queue: QueueState, today: CaseDate): Array<QueueItem & { until: CaseDate }> {
  return items.flatMap((x) => {
    const until = queue.snoozed[x.key];
    return until && isAfter(until, today) && !queue.dismissed[x.key] ? [{ ...x, until }] : [];
  });
}

export function inQueueScope(items: readonly QueueItem[], scope: Scope): QueueItem[] {
  if (scope === "mine") return items.filter((x) => x.mine);
  if (scope === "team") return items.filter((x) => !x.mine && x.hasCase);
  return items.filter((x) => x.kind === "unassigned");
}

/* ---------- Today: three bands by urgency ---------- */

export type Band = "now" | "soon" | "later";
export const bandOf = (dueDays: number): Band => (dueDays <= 30 ? "now" : dueDays <= 120 ? "soon" : "later");

export interface QueueBand<T> {
  band: Band;
  label: string;
  items: T[];
}

export function groupByBand<T extends { dueDays: number }>(items: readonly T[], t: Translate): Array<QueueBand<T>> {
  const labels: Record<Band, string> = { now: t("Within 30 days"), soon: t("31–120 days"), later: t("Later") };
  return (["now", "soon", "later"] as const)
    .map((band) => ({ band, label: labels[band], items: items.filter((x) => bandOf(x.dueDays) === band) }))
    .filter((group) => group.items.length > 0);
}

/* ---------- Action queue view: grouped by kind of work ---------- */

const QUEUE_GROUPS: Array<[string, readonly TodoKind[]]> = [
  ["Approvals", ["approve", "policy", "sigok", "nout", "alt"]],
  ["Emails & supplier replies", ["send", "recipient", "remind", "reply"]],
  ["Decisions & terms", ["decide", "agree", "draft"]],
  ["Documents & signatures", ["sign", "fields"]],
  ["Plans & notices", ["plan", "notice"]],
  ["Deadlines without owner", ["unassigned"]],
];

export function groupByKind<T extends { kind: TodoKind }>(items: readonly T[], t: Translate): Array<{ label: string; items: T[] }> {
  return QUEUE_GROUPS.map(([label, kinds]) => ({ label: t(label), items: items.filter((x) => kinds.includes(x.kind)) })).filter((g) => g.items.length > 0);
}

/* ---------- row actions ---------- */

const APPROVAL_KINDS: readonly TodoKind[] = ["approve", "policy", "sigok", "nout", "alt"];

export type RowCta =
  | { do: "open"; label: string }
  | { do: "startAssign"; label: string; assignTo: string }
  | { do: "startSelf"; label: string };

export interface RowActions {
  primary: RowCta;
  secondary: RowCta | null;
  /** Delegate an approval, or hand the case to someone else (the approver only). */
  delegate: { label: string; do: "delegate" | "transfer"; who: string } | null;
}

/** The person to delegate or reassign to: someone on the case other than the approver. */
function otherPerson(c: Case): string | null {
  return c.members.find((m) => m !== c.approver) ?? null;
}

export function rowActions(item: QueueItem, viewer: string, c: Case | undefined, t: Translate): RowActions {
  let primary: RowCta;
  let secondary: RowCta | null = null;
  if (item.kind === "unassigned") {
    if (viewer === DEMO_APPROVER) {
      primary = { do: "startAssign", label: t("Start & assign to {name}", { name: DEMO_OWNER.split(" ")[0] ?? DEMO_OWNER }), assignTo: DEMO_OWNER };
      secondary = { do: "startSelf", label: t("I’ll handle it") };
    } else {
      primary = { do: "startSelf", label: t("Start renewal") };
    }
  } else {
    primary = { do: "open", label: item.who === viewer ? t("Open at this step") : t("Open the case") };
  }

  let delegate: RowActions["delegate"] = null;
  if (c && viewer === c.approver) {
    const other = otherPerson(c);
    if (other) {
      const first = other.split(" ")[0] ?? other;
      if (APPROVAL_KINDS.includes(item.kind) && item.who === viewer) delegate = { label: t("Delegate to {name}", { name: first }), do: "delegate", who: other };
      else if (!APPROVAL_KINDS.includes(item.kind) && item.owner !== other) delegate = { label: t("Reassign to {name}", { name: first }), do: "transfer", who: other };
    }
  }
  return { primary, secondary, delegate };
}

export function initialsOf(name: string): string {
  return name
    .split(" ")
    .map((w) => w[0] ?? "")
    .join("");
}
