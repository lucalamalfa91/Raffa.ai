import type { PortfolioListItem } from "../../api/client";
import { isDeadlineCritical } from "../../styles/semantics";

/**
 * Attention/severity/sort logic for the Portfolio screen (route `/contracts`, ADR-018; screens.md #4;
 * task E07/F01/US01/T01, us-01-portfolio-list-filters AC-2 "attention strip" / AC-3 "sorted by
 * severity -> deadline; critical rows tinted + red bar"). ADR-019 names this column-one, not
 * colour-only ("urgency in column one, not colour-only" -- this task's own "Architecture decisions in
 * force").
 *
 * The four attention-strip buckets, their exact labels, and the per-row severity/issue-text branching
 * below are not invented -- they are read back verbatim from the compiled Claude Design prototype
 * (`inputs/design/prototypes/day1-demo.html`)'s own portfolio view-model (the `attDef` array and the
 * `allContracts.map(...)` issue/severity derivation next to it), the same "cite, don't invent" rule
 * ADR-019/ADR-020 set for this pass. Two adaptations were necessary because `GET /api/contracts`
 * (`PortfolioListItem`, `src/api/client.ts`) does not carry every field the prototype's own mock data
 * does:
 *  - The prototype's `status` field takes exactly `'Failed' | 'Processing' | 'Needs review' | ...`;
 *    the real `PortfolioListItem.status` is `Contract.Status`, a free-text business-status string
 *    (bootstrap default is the literal `processing`, otherwise whatever the metadata extraction stage
 *    last wrote -- see `PortfolioFilter.cs`'s own doc comment). This module treats it
 *    case-insensitively and treats "contains 'review'" as the needs-review signal, rather than
 *    requiring an exact `'Needs review'` match a free-text field can't guarantee.
 *  - The prototype's `risk` field is a flat `'High' | ...`; the real domain
 *    (`Contigo.Documents.Contracts.Domain.RiskSeverity`) has a fourth tier, `Critical`, which ADR-019's
 *    own locked semantic-mapping table does not define a treatment for. This module folds `Critical`
 *    into the same "high risk" bucket as `High` (the more-severe-leaning, conservative direction) --
 *    see `portfolioTableFormatters.ts#getPortfolioRiskTag` for the equivalent fold on the tag-colour side.
 *  - The prototype's issue text for the risk-only case includes an "uplift" figure
 *    (`'High risk · uplift ' + c.uplift`); `uplift` is a Renewals/Quote-module concept with no
 *    equivalent field on `PortfolioListItem`, so that clause is dropped rather than fabricated.
 */

export type AttentionSeverity = 0 | 1 | 2 | 3;

export interface AttentionRow {
  item: PortfolioListItem;
  /** Whole days from `now` to `item.cancellationDeadline`, or `null` when that date is not recorded. Negative once the deadline has passed. */
  cancelDays: number | null;
  /** 3 = most urgent (a red, tinted row); 0 = nothing flagged. */
  severity: AttentionSeverity;
  /** Column-one text (AC-3 "urgency in column one, not colour-only"). Never empty. */
  issue: string;
  isDeadlineSoon: boolean;
  isNeedsReview: boolean;
  isFailedOrProcessing: boolean;
  isHighRisk: boolean;
}

/**
 * Whole days from `now` to `dateOnly` (an OpenAPI `date`-formatted `yyyy-MM-dd` string, i.e. a
 * `DateOnly` on the wire -- see `openapi/contigo-api.v1.json`'s `getPortfolio` operation). `null` in,
 * `null` out. `now` defaults to the real clock but is an explicit parameter so callers (and this
 * module's own tests) can pin it -- the same reason `documents/documentTable.ts#formatUploadedAt`
 * fixes its own formatter's timezone rather than trusting the host clock.
 */
export function daysUntil(dateOnly: string | null, now: Date = new Date()): number | null {
  if (dateOnly === null) return null;

  const [year, month, day] = dateOnly.split("-").map(Number);
  // UTC midnight on both sides -- dateOnly has no time-of-day component, and comparing it against a
  // local-time `now` would shift the day count by one near midnight in any timezone west of UTC.
  const deadlineUtc = Date.UTC(year, month - 1, day);
  const todayUtc = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate());

  return Math.round((deadlineUtc - todayUtc) / 86_400_000);
}

function normalizeStatus(status: string): string {
  return status.trim().toLowerCase();
}

/** `RiskSeverity.High` or `.Critical` -- see this file's header comment on why `Critical` folds in here. */
function isHighOrCriticalRisk(risk: PortfolioListItem["risk"]): boolean {
  return risk === "High" || risk === "Critical";
}

/**
 * One row's attention/severity (AC-2/AC-3). Mirrors day1-demo.html's own `allContracts.map(...)`
 * branching order exactly (most urgent test first, first match wins) -- see this file's header
 * comment for the two deliberate field-availability adaptations.
 */
export function computeAttentionRow(item: PortfolioListItem, now: Date = new Date()): AttentionRow {
  const cancelDays = daysUntil(item.cancellationDeadline, now);
  const normalizedStatus = normalizeStatus(item.status);
  const isFailed = normalizedStatus === "failed";
  const isProcessing = normalizedStatus === "processing";
  const isNeedsReview = normalizedStatus.includes("review");
  // Reuses styles/semantics.ts's own locked threshold (ADR-019 "Deadline <= 45 days") rather than
  // re-deriving it -- semantics.ts's own header comment: "every screen ... uses the same threshold
  // ... never a re-derived one".
  const isDeadlineSoon = cancelDays !== null && isDeadlineCritical(cancelDays);
  const isHighRisk = isHighOrCriticalRisk(item.risk);

  let issue = "No action needed";
  let severity: AttentionSeverity = 0;

  if (isFailed) {
    issue = "Processing failed — re-upload";
    severity = 3;
  } else if (isNeedsReview && isDeadlineSoon) {
    issue = `Needs review · notice due in ${cancelDays} d`;
    severity = 3;
  } else if (isDeadlineSoon) {
    issue = `Cancellation notice due in ${cancelDays} d`;
    severity = 3;
  } else if (isNeedsReview) {
    issue = "Needs review";
    severity = 2;
  } else if (isProcessing) {
    issue = "Processing…";
    severity = 1;
  } else if (isHighRisk) {
    issue = "High risk";
    severity = 2;
  }

  return {
    item,
    cancelDays,
    severity,
    issue,
    isDeadlineSoon,
    isNeedsReview,
    isFailedOrProcessing: isFailed || isProcessing,
    isHighRisk,
  };
}

/**
 * AC-3 "sorted by severity -> deadline": highest severity first, then soonest cancellation deadline
 * (a row with no deadline sorts after every row that has one), then the contract id as a final,
 * deterministic tiebreak so two equally-urgent rows never reorder between renders. screens.md #4's own
 * prose names a third tier ("severity → deadline → score"); `score` is a Renewal-pipeline concept
 * (screens.md #8's own "Score" column) with no equivalent field on `PortfolioListItem`, and the parent
 * story's own governing AC-3 does not name it either -- dropped rather than fabricated, the same
 * "cite, don't invent" treatment this file's header comment gives the prototype's `uplift` field.
 */
export function compareBySeverityThenDeadline(a: AttentionRow, b: AttentionRow): number {
  if (a.severity !== b.severity) return b.severity - a.severity;

  const aDays = a.cancelDays ?? Number.POSITIVE_INFINITY;
  const bDays = b.cancelDays ?? Number.POSITIVE_INFINITY;
  if (aDays !== bDays) return aDays - bDays;

  return a.item.contractId.localeCompare(b.item.contractId);
}

export type AttentionBucketKey = "deadline" | "review" | "failed" | "risk";

export interface AttentionBucketDefinition {
  key: AttentionBucketKey;
  /** Exact label text, quoted verbatim from day1-demo.html's own `attDef` array / this task's AC-2. */
  label: string;
  meta: string;
  test: (row: AttentionRow) => boolean;
}

export const ATTENTION_BUCKETS: readonly AttentionBucketDefinition[] = [
  {
    key: "deadline",
    label: "Deadlines < 45 d",
    meta: "Cancellation notice due soon",
    test: (row) => row.isDeadlineSoon,
  },
  {
    key: "review",
    label: "Need review",
    meta: "Contract status needs review",
    test: (row) => row.isNeedsReview,
  },
  {
    key: "failed",
    label: "Failed / processing",
    meta: "Documents not yet usable",
    test: (row) => row.isFailedOrProcessing,
  },
  {
    key: "risk",
    label: "High risk",
    meta: "High or critical recorded risk",
    test: (row) => row.isHighRisk,
  },
];

export interface AttentionBucketCount extends AttentionBucketDefinition {
  count: number;
}

/** AC-2's four big numbers, always computed from the *whole* portfolio -- never the currently filtered view (see index.tsx's own header comment on why). */
export function computeAttentionBucketCounts(rows: readonly AttentionRow[]): readonly AttentionBucketCount[] {
  return ATTENTION_BUCKETS.map((bucket) => ({ ...bucket, count: rows.filter(bucket.test).length }));
}
