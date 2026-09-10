import type { RenewalActionStatusValue, RenewalPipelineItemBody } from "../../api/client";
import type { SemanticTag } from "../../styles/semantics";
import { isDeadlineCritical } from "../../styles/semantics";
import type { TrackedRenewalAction } from "./renewalActionStore";

/**
 * Pure view-model helpers for the V2 Renewals screen (route `/renewals`; ADR-024 V2 IA amending
 * ADR-020 screen 8; screens-v2.md #7; `raffa-v2/markup.html` "RENEWALS" block, `app.jsx`
 * `renewals` / `rsel` / `rnSummary` / `rAct`). No React here -- every rule is unit-testable without
 * rendering (`renewalPipelineViewModel.test.ts`).
 *
 * The Day-1 screen's threshold strip (0-30 … 270-365 d window buckets, "click = filter") is not part
 * of the V2 design and was removed with it: V2 is one list "sorted by priority (`score`)" with the
 * selected row's own "Why it is here" pane beside it. Values below are quoted from `app.jsx`'s own
 * `renewals` builder (`scoreFg`, `cancelFg`/`cancelW`, `st`/`stTag`, `bg`/`bar`) and `rnSummary`.
 */

/** One row of the priority list: the pipeline item, its own `GET /api/renewals/{id}/priority` score
 * (`null` while unresolved or failed -- rendered "—", never fabricated), and this browser's own
 * session-local action state (`./renewalActionStore.ts`). */
export interface RenewalTableRow {
  item: RenewalPipelineItemBody;
  score: number | null;
  tracked: TrackedRenewalAction | null;
}

/**
 * `app.jsx`: `renewals = completedCids.map(...).sort((a,b)=>b.score-a.score)` -- highest priority
 * first. A row whose score is still unknown sorts after every scored row (an honest "not ranked yet",
 * not a fabricated zero); ties break on the sooner notice deadline, then on the contract id so two
 * equal rows never swap between renders.
 */
export function buildRenewalRows(
  items: readonly RenewalPipelineItemBody[],
  scores: Readonly<Record<string, number | null>>,
  trackedByContract: ReadonlyMap<string, TrackedRenewalAction>,
): RenewalTableRow[] {
  return items
    .map((item) => ({ item, score: scores[item.contractId] ?? null, tracked: trackedByContract.get(item.contractId) ?? null }))
    .sort(compareByPriority);
}

function compareByPriority(a: RenewalTableRow, b: RenewalTableRow): number {
  if (a.score !== b.score) {
    if (a.score === null) return 1;
    if (b.score === null) return -1;
    return b.score - a.score;
  }
  const noticeA = a.item.daysUntilCancellationDeadline;
  const noticeB = b.item.daysUntilCancellationDeadline;
  if (noticeA !== noticeB) {
    if (noticeA === null) return 1;
    if (noticeB === null) return -1;
    return noticeA - noticeB;
  }
  return a.item.contractId < b.item.contractId ? -1 : a.item.contractId > b.item.contractId ? 1 : 0;
}

/** `rnSummary` when the tier is off (`kbOff`), quoted from `app.jsx`. */
export const RENEWALS_SUMMARY_OFF = "Computed from validated end dates and notice periods";

/** `rnSummary` when lit: "N contract(s) with validated dates · sorted by priority". */
export function formatRenewalsSummary(rowCount: number): string {
  if (rowCount === 0) return RENEWALS_SUMMARY_OFF;
  return `${rowCount} contract${rowCount === 1 ? "" : "s"} with validated dates · sorted by priority`;
}

/**
 * "Score" column emphasis, quoted from `app.jsx`'s own `scoreFg:c.score>=80?'var(--color-accent-700)'
 * :'inherit'` -- text, not colour-only (ADR-019 accessibility baseline): the number itself is always
 * shown, this only adds emphasis on top.
 */
export function isHighPriorityScore(score: number): boolean {
  return score >= 80;
}

/** `null` (the per-contract priority call has not resolved yet, or failed) renders as "—" -- never a fabricated score. */
export function formatScore(score: number | null): string {
  return score === null ? "—" : String(Math.round(score));
}

/** "Renews in" / "Notice in" cells: `{{ r.days }} d` / `{{ r.cancelDays }} d`, an honest "—" when the engine could not determine the date. */
export function formatDays(days: number | null): string {
  return days === null ? "—" : `${days} d`;
}

/** `cancelFg`/`cancelW`: the notice cell turns accent-700 + 600 within the locked 45-day window (`styles/semantics.ts#isDeadlineCritical`, never re-derived). */
export function isNoticeUrgent(daysUntilCancellationDeadline: number | null): boolean {
  return daysUntilCancellationDeadline !== null && isDeadlineCritical(daysUntilCancellationDeadline);
}

/**
 * "Supplier · contract" column. `RenewalPipelineItemBody` carries no contract name or type at all
 * (unlike `GET /api/contracts`), so the contract half is the same short-id-plus-tooltip treatment
 * `../contracts/portfolioTableFormatters.ts#formatSupplier` established for the identical "no name
 * field" gap, applied to `contractId`.
 */
export function formatContractRef(contractId: string): { label: string; title: string } {
  return { label: `Contract ${contractId.slice(0, 8)}`, title: contractId };
}

/** Supplier half of the same column and of the pane's heading: the wire's own name (R-SUP-04), or an honest placeholder -- never an id fragment. */
export function formatRenewalSupplier(supplierName: string | null): string {
  return supplierName ?? "Supplier not resolved";
}

/** "Status" column default before this browser has acted on a row this session -- `app.jsx`: `st:act||'Open'`. */
export const DEFAULT_RENEWAL_STATUS_LABEL = "Open";

export function getRenewalStatusLabel(tracked: TrackedRenewalAction | null): string {
  return tracked?.action ?? DEFAULT_RENEWAL_STATUS_LABEL;
}

/** `stTag:act?'tag-accent':'tag-neutral'` -- an acted-on row gets the accent emphasis, an un-acted "Open" row stays neutral. */
export function getRenewalStatusTag(tracked: TrackedRenewalAction | null): SemanticTag {
  return tracked === null
    ? { variant: "neutral", label: DEFAULT_RENEWAL_STATUS_LABEL }
    : { variant: "accent", label: tracked.action };
}

/** Pane owner before this browser has acted on the selected row this session -- the wire carries no owner until a human sets one via `POST /api/renewals/{id}/action`, so this is an honest "nobody yet", never a fabricated name. */
export const UNASSIGNED_OWNER_LABEL = "Unassigned";

export function getInsightOwner(tracked: TrackedRenewalAction | null): string {
  return tracked?.owner ?? UNASSIGNED_OWNER_LABEL;
}

/** Pane heading (`markup.html`: "{{ rsel.supplier }} — {{ rsel.cancelDays }} days to notice"); without a determined deadline the heading says so rather than counting to nothing. */
export function formatPaneHeading(supplierName: string | null, daysUntilCancellationDeadline: number | null): string {
  const supplier = formatRenewalSupplier(supplierName);
  if (daysUntilCancellationDeadline === null) return `${supplier} — notice date not determined`;
  return `${supplier} — ${daysUntilCancellationDeadline} day${daysUntilCancellationDeadline === 1 ? "" : "s"} to notice`;
}

/**
 * The pane's actions (`markup.html` `rAct.negotiate` / `rAct.assign`: "Start negotiation" as the
 * primary block button, "Assign to me" as the secondary). `status`/`action` map each onto the real
 * backend's closed `RenewalActionStatus` vocabulary (NotStarted/InProgress/Completed -- the only
 * values `POST /api/renewals/{id}/action` accepts): "Start negotiation" is work actually begun;
 * "Assign to me" claims ownership without starting it, matching `RenewalActionStatus.NotStarted`'s
 * own doc comment. `owner` is not part of the plan: every action sends the signed-in `userLabel` as
 * the owner (see `index.tsx`) -- there is no separate assignee picker, so "who acted" and "who owns
 * it" are the same person. The Day-1 screen's third action, "Snooze to 90-day threshold", is not in
 * the V2 design and is no longer offered.
 */
export type RenewalActionKind = "negotiate" | "assign";

export interface RenewalActionPlan {
  status: RenewalActionStatusValue;
  action: string;
  buttonLabel: string;
  /** `.btn-primary` for the recommended first move, `.btn-secondary` for the rest (`markup.html`). */
  emphasis: "primary" | "secondary";
}

const RENEWAL_ACTION_PLANS: Readonly<Record<RenewalActionKind, RenewalActionPlan>> = {
  negotiate: { status: "InProgress", action: "In negotiation", buttonLabel: "Start negotiation", emphasis: "primary" },
  assign: { status: "NotStarted", action: "Assigned", buttonLabel: "Assign to me", emphasis: "secondary" },
};

export const RENEWAL_ACTION_KINDS: readonly RenewalActionKind[] = ["negotiate", "assign"];

export function getRenewalActionPlan(kind: RenewalActionKind): RenewalActionPlan {
  return RENEWAL_ACTION_PLANS[kind];
}
