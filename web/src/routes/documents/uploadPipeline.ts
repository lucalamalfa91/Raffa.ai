import type { DocumentProcessingStatus } from "../../api/client";
import { getStatusTag, type SemanticTag } from "../../styles/semantics";

/**
 * The 6-stage processing pipeline (screens.md #3: "Processing pipeline list
 * (6 stages, current pulsing)"). Labels are quoted verbatim from the
 * compiled Claude Design bundle's own `pipeLabels` array
 * (inputs/design/prototypes/day1-demo.html) -- the "pixel reference" ADR-020
 * names -- not invented. product-spec.md's own architecture line (§7:
 * "Upload -> Object Storage -> Processing Job -> Document Classification ->
 * Native Text Extraction / OCR if required -> Section + Table Detection ->
 * Structured AI Extraction -> Schema Validation -> Entity Resolution /
 * Normalization -> Canonical Data -> Embeddings / Search Index -> Ready /
 * Needs Review") describes the same pipeline at a finer grain; these six
 * labels are the prototype's own UI-facing compression of it, and this file
 * consumes that compression rather than re-deriving a different one.
 */
export const PIPELINE_STAGE_LABELS: readonly string[] = [
  "Uploaded to object storage",
  "Classifying document type",
  "Text extraction / OCR",
  "Section + table detection",
  "Structured AI extraction",
  "Schema validation & entity resolution",
];

/**
 * `POST /api/documents` runs the whole parse -> classify -> extract pipeline
 * *synchronously* before responding (task E02/F06/US01/T01 -- see
 * ../../api/client.ts's `uploadDocument` doc comment) -- there is no
 * server-sent per-stage event to drive this list from. `PIPELINE_STAGE_LABELS`
 * is therefore a client-side pacing animation shown *while the one upload
 * request is in flight*, the same role the compiled prototype's own
 * `s.uplStep` timer plays -- it is not a claim that the server has literally
 * reached stage N. index.tsx advances one stage at a time on this interval
 * and holds on the last stage if the real request outlives it; the request's
 * actual resolution always wins.
 */
export const PIPELINE_STEP_INTERVAL_MS = 900;

export type PipelineStageState = "done" | "current" | "pending";

export interface PipelineStageView {
  label: string;
  state: PipelineStageState;
}

/**
 * Pure view model for ProcessingPipeline.tsx: stage `i` is `done` while the
 * ticker is past it, `current` (pulsing) exactly at it, else `pending` --
 * mirrors the compiled prototype's own
 * `i<s.uplStep ? ... : i===s.uplStep ? ... : ...` ternary (fg/dot/anim) one
 * for one.
 */
export function getPipelineStageViews(currentStepIndex: number): PipelineStageView[] {
  return PIPELINE_STAGE_LABELS.map((label, index) => ({
    label,
    state: index < currentStepIndex ? "done" : index === currentStepIndex ? "current" : "pending",
  }));
}

/** AC-3's three outcomes (screens.md #3 "States: ... needs_review · completed · failed"). */
export type UploadOutcome = "needs_review" | "completed" | "failed";

/**
 * `DocumentProcessingStatus` (../../api/client.ts, contract-sourced) also
 * carries `"Uploaded"`/`"Processing"` for a job that has not reached a
 * terminal state. The V1 pipeline is synchronous (see
 * PIPELINE_STEP_INTERVAL_MS's own comment), so a `POST /api/documents`
 * response is not expected to carry either value today -- but the contract
 * keeps them, so index.tsx checks this guard instead of assuming.
 */
export function isTerminalProcessingStatus(
  status: DocumentProcessingStatus,
): status is "NeedsReview" | "Completed" | "Failed" {
  return status === "NeedsReview" || status === "Completed" || status === "Failed";
}

export function getUploadOutcome(status: "NeedsReview" | "Completed" | "Failed"): UploadOutcome {
  switch (status) {
    case "NeedsReview":
      return "needs_review";
    case "Completed":
      return "completed";
    case "Failed":
      return "failed";
  }
}

export interface ResultCardContent {
  tag: SemanticTag;
  ctaLabel: string;
  message: string;
}

/**
 * Tag variant/label reuse styles/semantics.ts#getStatusTag (ADR-019's locked
 * status mapping) -- never re-derived here, per that module's own "screens
 * call this instead of re-deriving thresholds" rule. Message/CTA copy is
 * adapted from the compiled prototype's own `uplMap` (day1-demo.html), with
 * two deliberate departures from its literal text, both flagged here rather
 * than silently copied verbatim:
 *   - the prototype's `completed`/`needs_review` messages ("41 fields
 *     extracted, all above 95% confidence.") describe one specific fixture
 *     document. The real `uploadDocument` response carries no field-level
 *     detail at all (openapi/contigo-api.v1.json's 201 body is
 *     id/contractId/fileName/mimeType/processingStatus/createdAt only), so
 *     this version names the uploaded file instead of fabricating a field
 *     count or confidence figure the API never returned.
 *   - the prototype's `failed` message asserts "the PDF is
 *     password-protected" as fact. The real `Failed` status carries no
 *     failure-reason field, so this version names password-protection /
 *     corruption as something to check, not a confirmed cause -- still
 *     plain-language and still names the failing job (ADR-019 accessibility
 *     baseline), never a raw stack trace or an invented specific.
 */
export function getResultCardContent(outcome: UploadOutcome, fileName: string): ResultCardContent {
  const tag = getStatusTag(outcome);
  switch (outcome) {
    case "completed":
      return {
        tag,
        ctaLabel: "Open Contract 360",
        message: `${fileName} was classified, extracted and structured. Every field cleared the confidence threshold.`,
      };
    case "needs_review":
      return {
        tag,
        ctaLabel: "Review extraction",
        message: `${fileName} was classified and extracted, but at least one field is below the 80% confidence threshold and needs your review.`,
      };
    case "failed":
      return {
        tag,
        ctaLabel: "Retry upload",
        message: `Contigo could not process ${fileName}. Check that it isn't password-protected or corrupted, then try again.`,
      };
  }
}
