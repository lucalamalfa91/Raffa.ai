/**
 * Contigo design system — semantic mappings (AC-2: "encoded, not colour-only").
 *
 * Ports the locked semantic mapping table into a single, typed, testable
 * source of truth, so every screen that renders a confidence/status/risk/
 * deadline value uses the same threshold and the same label -- never a
 * re-derived one, and never colour alone.
 *
 * Cites: reports/architecture/ADR-019-web-design-system.md ("Semantic
 * mapping (locked)"), inputs/design/prototypes/design-system.md ("Semantic
 * mapping"). Both carry spec §7.3's confidence thresholds (>95% accept,
 * 80-95% flag, <80% require review) onto the screen -- this module does not
 * re-derive them, only encodes the mapping already decided.
 *
 * Pair every `SemanticTag` with the `.tag`/`.tag-{variant}` classes in
 * src/styles/components.css; pair `isDeadlineCritical`/`isConfidenceBlocking`
 * with `.deadline-critical` / a disabled CTA + `.hint` respectively.
 */

/** `.tag-neutral` | `.tag-accent` | `.tag-outline` (src/styles/components.css). */
export type TagVariant = "neutral" | "accent" | "outline";

export interface SemanticTag {
  variant: TagVariant;
  label: string;
}

/**
 * Confidence >95% / 80-95% / <80% -> Accepted / Flagged / Review.
 * Below 80% blocks consequential use -- see `isConfidenceBlocking`.
 */
export function getConfidenceTag(confidencePct: number): SemanticTag {
  const rounded = Math.round(confidencePct);
  if (confidencePct > 95) {
    return { variant: "neutral", label: `Accepted · ${rounded}%` };
  }
  if (confidencePct >= 80) {
    return { variant: "accent", label: `Flagged · ${rounded}%` };
  }
  return { variant: "outline", label: `Review · ${rounded}%` };
}

/**
 * A confidence below 80% blocks consequential use (ADR-019 "Semantic
 * mapping"). Screens gate CTAs (e.g. disable "Mark as validated") on this
 * function, not on inspecting the tag's colour/variant.
 */
export function isConfidenceBlocking(confidencePct: number): boolean {
  return confidencePct < 80;
}

export type DocumentStatus = "completed" | "ready" | "needs_review" | "failed";

/** Status completed/Ready -> neutral; needs_review -> outline; failed -> accent. */
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
