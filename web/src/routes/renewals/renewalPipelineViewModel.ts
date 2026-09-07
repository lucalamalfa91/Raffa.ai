import type { RenewalActionStatusValue, RenewalPipelineItemBody } from "../../api/client";
import type { SemanticTag } from "../../styles/semantics";
import type { TrackedRenewalAction } from "./renewalActionStore";

/**
 * Pure view-model helpers for the Renewal pipeline screen (route `/renewals`, ADR-018; screens.md #8
 * "Renewal pipeline"; ADR-020 screen 8; task E08/F01/US01/T01, us-01-renewal-pipeline AC-1 threshold
 * strip / AC-2 table / AC-3 insight card + actions / AC-4 states). Same one-concern-per-file split
 * `../contracts/portfolioAttention.ts`/`../contracts/portfolioTableFormatters.ts` already established
 * for this repo: no React here, so every rule below is unit-testable without rendering anything.
 *
 * Values not already covered by `../contracts/portfolioAttention.ts` (`daysUntil`) or
 * `../contracts/portfolioTableFormatters.ts` (`formatAnnualSpend`, `formatDateOnly`,
 * `formatSupplier`) are quoted verbatim from `inputs/design/prototypes/day1-demo.html`'s own
 * renewals view-model (the `windows`/`allRenewals` builders), not invented -- see each export's own
 * comment for its exact citation.
 */

/**
 * AC-1 "Threshold strip 0-30 ... 270-365d with counts (click = filter)". Boundaries are quoted
 * verbatim from day1-demo.html's own `winDef=[365,270,180,120,90,60,30]` window list. Rendered here
 * ascending (0-30 first, 270-365 last) to match this task's own AC-1 wording and screens.md #8's "0-30
 * ... 270-365 d" -- the prototype's own `winDef` array is itself descending (its `windows.map`
 * produces "270-365 d" first), which is an implementation detail of that JS, not a locked visual
 * order.
 */
const RENEWAL_WINDOW_BOUNDARIES = [0, 30, 60, 90, 120, 180, 270, 365] as const;

export interface RenewalWindowBucket {
  key: string;
  /** En-dash, matching day1-demo.html's own `(winDef[i+1]||0)+'–'+w+' d'` label format and this task's own AC-1 text. */
  label: string;
  lowExclusiveDays: number;
  highInclusiveDays: number;
}

export const RENEWAL_WINDOWS: readonly RenewalWindowBucket[] = RENEWAL_WINDOW_BOUNDARIES.slice(0, -1).map(
  (low, index) => {
    const high = RENEWAL_WINDOW_BOUNDARIES[index + 1];
    return { key: `${low}-${high}`, label: `${low}–${high} d`, lowExclusiveDays: low, highInclusiveDays: high };
  },
);

/**
 * Bucket membership test, quoted from day1-demo.html's own `inWin=(c,w,i)=>{const
 * lo=winDef[i+1]||0; return c.days<=w&&c.days>lo}` (`c.days` is `daysUntilRenewal`, the same field
 * the table's own "Renews in" column renders -- not `daysUntilCancellationDeadline`). A row with no
 * determined renewal date (`daysUntilRenewal === null`, i.e. `RenewalCalculationStatus.CannotDetermine`
 * or `.NoRenewal`) matches no bucket at all -- it still appears in the full table, just uncounted and
 * unfilterable by this strip, the same "active=contracts.filter(c=>typeof c.days==='number')" gate
 * the prototype applies before building its own window counts.
 */
export function isInRenewalWindow(daysUntilRenewal: number | null, bucket: RenewalWindowBucket): boolean {
  return (
    daysUntilRenewal !== null && daysUntilRenewal > bucket.lowExclusiveDays && daysUntilRenewal <= bucket.highInclusiveDays
  );
}

export interface RenewalWindowCount extends RenewalWindowBucket {
  count: number;
}

/** AC-1's per-bucket counts, always computed from the whole pipeline -- never the currently filtered view (same "counts are portfolio-wide" rule `../contracts/portfolioAttention.ts#computeAttentionBucketCounts` already follows for its own strip). */
export function computeRenewalWindowCounts(items: readonly RenewalPipelineItemBody[]): readonly RenewalWindowCount[] {
  return RENEWAL_WINDOWS.map((bucket) => ({
    ...bucket,
    count: items.filter((item) => isInRenewalWindow(item.daysUntilRenewal, bucket)).length,
  }));
}

/** AC-1 "click = filter". `activeKey === null` (no bucket selected) returns `items` unchanged; an unknown key (should not happen -- `activeKey` only ever comes from a `RENEWAL_WINDOWS` member's own `key`) is treated the same as "no filter" rather than silently returning nothing. */
export function applyRenewalWindowFilter(
  items: readonly RenewalPipelineItemBody[],
  activeKey: string | null,
): readonly RenewalPipelineItemBody[] {
  if (activeKey === null) return items;
  const bucket = RENEWAL_WINDOWS.find((candidate) => candidate.key === activeKey);
  if (!bucket) return items;
  return items.filter((item) => isInRenewalWindow(item.daysUntilRenewal, bucket));
}

/**
 * AC-2 "Score" column colour. Quoted verbatim from day1-demo.html's own
 * `scoreFg:c.score>=80?'var(--color-accent-700)':'inherit'` -- text, not colour-only (ADR-019
 * accessibility baseline): the number itself is always shown, this only adds emphasis on top.
 */
export function isHighPriorityScore(score: number): boolean {
  return score >= 80;
}

/** `null` (the per-contract `GET /api/renewals/{contractId}/priority` call has not resolved yet, or failed) renders as "-" -- never a fabricated score. */
export function formatScore(score: number | null): string {
  return score === null ? "—" : String(Math.round(score));
}

/**
 * "Contract" column (AC-2). `RenewalPipelineItemBody` carries no contract name or type at all --
 * unlike `GET /api/contracts` (`PortfolioListItem.Type`, the proxy
 * `../contracts/portfolioTableFormatters.ts#getContractTypeLabel` uses for the identical gap on the
 * Portfolio screen), `GET /api/renewals`'s own response shape has no `type` field to fall back to
 * (see `RenewalPipelineItemBody` in `../../api/client.ts`). Cross-referencing a second, whole-portfolio
 * fetch just to resolve one column's label was judged not worth the added round trip and join
 * fragility for this pass -- this instead reuses the same short-id-plus-tooltip treatment
 * `../contracts/portfolioTableFormatters.ts#formatSupplier` already established for the identical
 * "no name field" gap on `supplierId`, applied to `contractId` instead.
 */
export function formatContractRef(contractId: string): { label: string; title: string } {
  return { label: `Contract ${contractId.slice(0, 8)}`, title: contractId };
}

/** AC-2 "Status" column default, before this browser has acted on a row this session. Quoted from day1-demo.html's own row-status fallback (`st:act||...'Open'`). */
export const DEFAULT_RENEWAL_STATUS_LABEL = "Open";

/** AC-2 "Status" column value: the free-text action this browser recorded this session (see `../renewals/renewalActionStore.ts`), or the honest default above when nothing has been actioned yet -- there is no way to read back a *previous* session's action (see that module's own header comment), so this table never claims a status this browser cannot actually know. */
export function getRenewalStatusLabel(tracked: TrackedRenewalAction | null): string {
  return tracked?.action ?? DEFAULT_RENEWAL_STATUS_LABEL;
}

/** AC-2 "Status" tag variant. Quoted from day1-demo.html's own `stTag:act||c.id===3?'tag-accent':'tag-neutral'` -- any acted-on row (regardless of which of the three actions) gets the same accent emphasis as "needs attention / already in motion"; an un-acted "Open" row stays neutral. */
export function getRenewalStatusTag(tracked: TrackedRenewalAction | null): SemanticTag {
  return tracked === null
    ? { variant: "neutral", label: DEFAULT_RENEWAL_STATUS_LABEL }
    : { variant: "accent", label: tracked.action };
}

/** Insight card "Owner" field (spec §9.3) before this browser has acted on the selected row this session -- `RenewalPipelineItemBody`/`RenewalInsightCard` carry no owner field at all (it only ever exists once a human sets one via `POST /api/renewals/{id}/action`), so this is an honest "nobody yet", never a fabricated name. */
export const UNASSIGNED_OWNER_LABEL = "Unassigned";

export function getInsightOwner(tracked: TrackedRenewalAction | null): string {
  return tracked?.owner ?? UNASSIGNED_OWNER_LABEL;
}

const NOT_YET_AVAILABLE = "Not yet available";

/** Insight card "Annual uplift" field (spec §9.3). `RenewalInsightRecommendations.AnnualUpliftPercent` is honestly `null` this wave (needs the R3 Benchmark/Savings modules, per that record's own doc comment) -- rendered as an honest gap, the same "Not yet available" treatment `../contracts/contract360/OverviewTab.tsx`'s own driver numbers already use for the identical field. */
export function formatUpliftPercent(annualUpliftPercent: number | null): string {
  if (annualUpliftPercent === null) return NOT_YET_AVAILABLE;
  return `${annualUpliftPercent > 0 ? "+" : ""}${annualUpliftPercent}%`;
}

/** Insight card "Market position" field (spec §9.3). Same honest-gap treatment as `formatUpliftPercent` above -- `RenewalInsightRecommendations.MarketPosition` is `null` until the R3 Benchmark module exists. */
export function formatMarketPosition(marketPosition: string | null): string {
  return marketPosition ?? NOT_YET_AVAILABLE;
}

/** Insight card "Potential savings" field (spec §9.3). Same honest-gap treatment as `formatUpliftPercent` above -- `RenewalInsightRecommendations.PotentialSavingsRange` is `null` until the R3 Savings module exists. */
export function formatPotentialSavings(potentialSavingsRange: string | null): string {
  return potentialSavingsRange ?? NOT_YET_AVAILABLE;
}

/**
 * AC-3's three fixed actions (screens.md #8: "actions Start negotiation / Assign to me / Snooze").
 * `status`/`action` are quoted verbatim from day1-demo.html's own
 * `rAct={negotiate:()=>setAct('In negotiation'),assign:()=>setAct('Assigned'),snooze:()=>setAct('Snoozed
 * to 90 d')}`; `buttonLabel` is quoted from the same block's three `<button>` text nodes ("Start
 * negotiation", "Assign to me", "Snooze to 90-day threshold"). `status` maps each free-text action onto
 * the real backend's closed `RenewalActionStatus` vocabulary (NotStarted/InProgress/Completed, the only
 * values `POST /api/renewals/{id}/action` accepts) -- "Start negotiation" is the only one of the three
 * that represents work actually begun; "Assign to me"/"Snooze" both claim ownership without yet
 * starting work, matching `RenewalActionStatus.NotStarted`'s own doc comment ("the default a
 * Procurement user starts from"). `owner` is deliberately not part of this plan: every action sends
 * this screen's own signed-in `userLabel` as the owner (see `index.tsx`) -- there is no separate
 * assignee picker in V1, so "who acted" and "who owns it" are always the same person.
 */
export type RenewalActionKind = "negotiate" | "assign" | "snooze";

export interface RenewalActionPlan {
  status: RenewalActionStatusValue;
  action: string;
  buttonLabel: string;
}

const RENEWAL_ACTION_PLANS: Readonly<Record<RenewalActionKind, RenewalActionPlan>> = {
  negotiate: { status: "InProgress", action: "In negotiation", buttonLabel: "Start negotiation" },
  assign: { status: "NotStarted", action: "Assigned", buttonLabel: "Assign to me" },
  snooze: { status: "NotStarted", action: "Snoozed to 90 d", buttonLabel: "Snooze to 90-day threshold" },
};

export const RENEWAL_ACTION_KINDS: readonly RenewalActionKind[] = ["negotiate", "assign", "snooze"];

export function getRenewalActionPlan(kind: RenewalActionKind): RenewalActionPlan {
  return RENEWAL_ACTION_PLANS[kind];
}

/**
 * One row of the populated table + the currently-selected insight card (AC-2/AC-3). `score` is
 * `null` while this row's own `GET /api/renewals/{contractId}/priority` call has not resolved yet, or
 * failed -- rendered as "-" (`formatScore`), never a fabricated number. `tracked` is this browser's
 * own session-local action state for this contract (`../renewals/renewalActionStore.ts`), or `null`
 * before anything has been actioned.
 */
export interface RenewalTableRow {
  item: RenewalPipelineItemBody;
  score: number | null;
  tracked: TrackedRenewalAction | null;
}
