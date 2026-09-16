/**
 * Raffa design system — semantic mappings (AC-2: "encoded, not colour-only").
 *
 * Ports the locked semantic mapping table into a single, typed, testable
 * source of truth, so every screen that renders a status/risk/deadline value
 * uses the same label -- never a re-derived one, and never colour alone.
 *
 * Confidence is label-only (ADR-019 w17 clause 7; ADR-012 w17 clause 34):
 * `getConfidenceTag` paints the **server's** persisted decision
 * (`auto_accepted` / `human_accepted` / `review_required`). It does not
 * decide acceptance and does not compare a percentage against a bar.
 * Percentages beside a decision are floored (ADR-019 w17 clause 8).
 *
 * Pair every `SemanticTag` with the `.tag`/`.tag-{variant}` classes in
 * src/styles/components.css; pair `isDeadlineCritical` with `.deadline-critical`.
 */

/** `.tag-neutral` | `.tag-accent` | `.tag-outline` (src/styles/components.css). */
export type TagVariant = "neutral" | "accent" | "outline";

export interface SemanticTag {
  variant: TagVariant;
  label: string;
}

/** The three persisted extraction decisions `GET /api/contracts/{id}/evidence` returns. */
export type ConfidenceDecision = "auto_accepted" | "human_accepted" | "review_required";

/**
 * Paints a label for a server decision. Does not decide acceptance.
 *
 * Signature stays `(confidencePct: number, …)` so Contract 360 call sites
 * compile untouched (E22/F04 deletes those). A missing decision is painted
 * as `review_required` — never recomputed from the percentage.
 * `auto_accepted` and `human_accepted` share `.tag-neutral` and differ only
 * in the label (ADR-019 accessibility baseline: text carries the meaning).
 */
export function getConfidenceTag(confidencePct: number, decision: ConfidenceDecision = "review_required"): SemanticTag {
  const floored = Math.floor(confidencePct);
  switch (decision) {
    case "auto_accepted":
      return { variant: "neutral", label: `Accepted automatically · ${floored}%` };
    case "human_accepted":
      return { variant: "neutral", label: "Accepted by you" };
    case "review_required":
      return { variant: "outline", label: `Review · ${floored}%` };
  }
}

export type DocumentStatus = "completed" | "ready" | "needs_review" | "failed" | "processing" | "rejected" | "uploaded";

/**
 * Status completed/Ready -> neutral; needs_review -> outline; failed -> accent; processing ->
 * neutral (task E13/F09/US01/T03, web-documents-v2: a document row is visible from the moment it
 * is picked, R-DOC-01 AC-1 -- V1 never rendered a `processing` row in a status tag at all, since its
 * synchronous upload pipeline only ever wrote a table row once a document reached a terminal status;
 * see `raffa-v2/app.jsx`'s own `docRows` map, `processing:{tag:'tag-neutral',...}`); rejected ->
 * outline "Not added" (ADR-019 w15 clause 1, task E16/F03/US01/T01: a refusal is a decision about
 * the file, not a Raffa-side failure the user can retry, so it takes the system's "this row is
 * about a decision" treatment -- the same `.tag-outline` the retired "Not added" card carried --
 * and never `.tag-accent`, which stays reserved for `failed`); uploaded -> neutral "Uploaded" (ADR-019
 * w15 round-3 clause 1, task E16/F03/US02/T02, wave w15: the row the user sees the instant a file is
 * picked or the server first stores it, before any Worker stage has run -- deliberately the same
 * neutral variant as `processing`, since nothing has gone wrong and nothing needs the user yet; the
 * *label* is what carries "there is genuinely nothing to look at here", never colour alone).
 */
export function getStatusTag(status: DocumentStatus): SemanticTag {
  switch (status) {
    case "completed":
      return { variant: "neutral", label: "Completed" };
    case "ready":
      return { variant: "neutral", label: "Ready" };
    case "needs_review":
      return { variant: "outline", label: "Needs review" };
    case "failed":
      return { variant: "accent", label: "Failed" };
    case "processing":
      return { variant: "neutral", label: "Processing" };
    case "rejected":
      return { variant: "outline", label: "Not added" };
    case "uploaded":
      return { variant: "neutral", label: "Uploaded" };
  }
}

export type RiskLevel = "high" | "medium" | "low";

/** Risk High -> accent; Medium/Low -> neutral. */
export function getRiskTag(risk: RiskLevel): SemanticTag {
  if (risk === "high") {
    return { variant: "accent", label: "High risk" };
  }
  return { variant: "neutral", label: risk === "medium" ? "Medium risk" : "Low risk" };
}

/**
 * Deadline <=45 days renders in `.deadline-critical` (--color-accent-700,
 * weight 600). Always pair with the day count in the label -- never rely on
 * colour alone to say "urgent".
 */
export function isDeadlineCritical(daysUntilDeadline: number): boolean {
  return daysUntilDeadline <= 45;
}
