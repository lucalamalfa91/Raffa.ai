import type { Contract360Body, CorrectionHistoryEntryBody, PortfolioContractType } from "../../../api/client";
import { getConfidenceTag, isConfidenceBlocking, type SemanticTag } from "../../../styles/semantics";
import { formatDateOnly, getContractTypeLabel } from "../portfolioTableFormatters";

/**
 * Pure view-model helpers for the Review / correction screen (route `/contracts/:contractId/review`,
 * ADR-018; ADR-020 screen 6; task E07/F03/US01/T01, us-01-field-review-correction AC-1/AC-2/AC-3/
 * AC-4). Same one-concern-per-file split `../contract360/contract360ViewModel.ts` already
 * established for this folder: no React here, so every rule below is unit-testable without
 * rendering anything.
 *
 * **Field catalogue mirrors the backend exactly, on purpose.** `CORRECTABLE_FIELDS` below is a
 * client-side copy of `ContractCorrectionService.CorrectableFields`'s key set + value kind
 * (backend/src/Contigo.Documents.Contracts/Application/ContractCorrectionService.cs) -- the only
 * field names `PATCH /api/contracts/{id}` (`correctContract`) will ever accept, and the ones the
 * story's own "E02 correction API (assumed)" dependency names. This screen reviews exactly that
 * set, not the Products/Clauses/Obligations/Risks line items Contract 360's "Needs your attention"
 * surfaces (`../contract360/contract360ViewModel.ts#computeNeedsAttention`) -- those already carry
 * real per-field confidence via `GET /api/contracts/{id}`, but have no correction endpoint of their
 * own yet (a separate, pre-existing gap this task does not touch).
 *
 * **No live confidence score exists yet for this field set.** `ExtractionEvidence`
 * (backend/src/Contigo.Documents.Contracts/Domain/ExtractionEvidence.cs, added by task
 * E02/F01/US02/T01) stores exactly the per-field confidence/source-span/extraction-job trail AC-2
 * and AC-3 describe -- keyed by the same `FieldName` scheme `CorrectionHistory` already uses -- but
 * no endpoint anywhere in `backend/src/Contigo.Api` reads it (checked: grep for "Evidence" across
 * that project matches nothing contract-shaped), and `Contract360QueryService`
 * (`GET /api/contracts/{id}`) never joins it either. This is a genuine, pre-existing backend gap,
 * not one this web-only task's file scope can close (`ContractsEndpointExtensions.cs` is not in
 * this task's "Files to create or modify"). `ContractEvidenceSchemaTests.cs`'s own doc comment even
 * predates `ExtractionEvidence` by 12 minutes of migration timestamps and says the opposite
 * ("`Contract`'s own scalar fields ... [are] populated from evidenced `Clause` rows rather than
 * carrying parallel per-field evidence columns of their own") -- the two were never reconciled.
 *
 * Rather than fabricate a percentage this screen cannot back with a real number (Appendix C rule
 * 10 -- already this codebase's own convention, e.g. `contract360ViewModel.ts#buildRecommendation`'s
 * "Not yet available" fields), every undecided field is conservatively treated as spec §7.3's most
 * cautious <80% "must be reviewed" band until a human resolves it -- see `isFieldBlocking` below.
 * The mapping function itself (`fieldTag`) still takes a real 0-100 `confidencePct` when one is
 * available and reuses `styles/semantics.ts#getConfidenceTag` verbatim (never re-derived), so a
 * future task that adds the missing evidence-read endpoint only has to populate
 * `ReviewFieldRow.confidencePct` -- zero change to this file's gating/tag logic.
 */

export type CorrectableFieldName =
  | "type"
  | "status"
  | "currency"
  | "startDate"
  | "endDate"
  | "effectiveDate"
  | "cancellationDeadline"
  | "annualSpend"
  | "totalContractValue"
  | "autoRenewal"
  | "renewalTermMonths"
  | "paymentTerms"
  | "governingLaw";

export type FieldKind = "enum" | "text" | "date" | "decimal" | "int" | "bool";

export interface FieldDefinition {
  name: CorrectableFieldName;
  label: string;
  kind: FieldKind;
  /** Mirrors whether `ContractCorrectionService.CorrectableFields` built this entry with
   * `RequiredText`/`RequiredEnum`/`RequiredBool` (true) or an `OptionalX` builder (false). A blank
   * correction-form submission maps to `null` (clear the field) only when `required` is false --
   * the backend rejects a null/empty value for a required field outright, so sending `null` for one
   * would only trade an honest "cannot be cleared" 400 for a less obviously-related one. */
  required: boolean;
}

/** Order mirrors `ContractCorrectionService.CorrectableFields`' own declaration order verbatim. */
export const CORRECTABLE_FIELDS: readonly FieldDefinition[] = [
  { name: "type", label: "Contract type", kind: "enum", required: true },
  { name: "status", label: "Status", kind: "text", required: true },
  { name: "currency", label: "Currency", kind: "text", required: true },
  { name: "startDate", label: "Start date", kind: "date", required: false },
  { name: "endDate", label: "End date", kind: "date", required: false },
  { name: "effectiveDate", label: "Effective date", kind: "date", required: false },
  { name: "cancellationDeadline", label: "Cancellation deadline", kind: "date", required: false },
  { name: "annualSpend", label: "Annual spend", kind: "decimal", required: false },
  { name: "totalContractValue", label: "Total contract value (TCV)", kind: "decimal", required: false },
  { name: "autoRenewal", label: "Auto-renewal", kind: "bool", required: true },
  { name: "renewalTermMonths", label: "Renewal term (months)", kind: "int", required: false },
  { name: "paymentTerms", label: "Payment terms", kind: "text", required: false },
  { name: "governingLaw", label: "Governing law", kind: "text", required: false },
];

/** `ContractDocumentType`'s six members (`../portfolioTableFormatters.ts#CONTRACT_TYPE_LABEL`'s own
 * keys) -- the only values the `type` field's correction `<select>` may submit. */
export const CONTRACT_TYPE_OPTIONS: readonly PortfolioContractType[] = [
  "Msa",
  "OrderForm",
  "Amendment",
  "Sow",
  "RenewalLetter",
  "Other",
];

/**
 * Raw wire-format value currently on `Contract`, read from the already-fetched Contract 360
 * aggregate (no extra request needed -- `getContract360`'s header/tabs already carry every
 * correctable field's current value). `null` when this optional field has never been extracted.
 * Mirrors `ContractCorrectionService`'s own per-field `Read` table exactly (same canonical string
 * shapes this screen must also send back: dates `yyyy-MM-dd`, decimals/ints via plain `String()`,
 * bool as `"true"`/`"false"`).
 */
export function readCorrectableValue(contract: Contract360Body, name: CorrectableFieldName): string | null {
  const { header, tabs } = contract;
  switch (name) {
    case "type":
      return header.type;
    case "status":
      return header.status;
    case "currency":
      return tabs.overview.currency;
    case "startDate":
      return header.startDate;
    case "endDate":
      return header.endDate;
    case "effectiveDate":
      return tabs.overview.effectiveDate;
    case "cancellationDeadline":
      return header.cancellationDeadline;
    case "annualSpend":
      return header.annualSpend === null ? null : String(header.annualSpend);
    case "totalContractValue":
      return header.totalContractValue === null ? null : String(header.totalContractValue);
    case "autoRenewal":
      return header.autoRenewal ? "true" : "false";
    case "renewalTermMonths":
      return tabs.overview.renewalTermMonths === null ? null : String(tabs.overview.renewalTermMonths);
    case "paymentTerms":
      return tabs.overview.paymentTerms;
    case "governingLaw":
      return tabs.overview.governingLaw;
  }
}

/** Human-readable rendering of a field's raw wire value, for the list column and evidence pane --
 * never fed back into the correction form (that always starts from the raw value; see
 * `ReviewFieldRow.rawValue`). */
export function formatCorrectableValue(kind: FieldKind, name: CorrectableFieldName, rawValue: string | null): string {
  if (rawValue === null) return "—";
  switch (kind) {
    case "date":
      return formatDateOnly(rawValue);
    case "decimal":
    case "int":
      return new Intl.NumberFormat("en-GB").format(Number(rawValue));
    case "bool":
      return rawValue === "true" ? "Yes" : "No";
    case "enum":
      return name === "type" ? getContractTypeLabel(rawValue as PortfolioContractType) : rawValue;
    default:
      return rawValue;
  }
}

export type ReviewDecision = "pending" | "accepted" | "corrected";

export interface ReviewFieldRow {
  name: CorrectableFieldName;
  label: string;
  kind: FieldKind;
  /** See `FieldDefinition.required`'s own doc comment. */
  required: boolean;
  /** Canonical wire-format value (never `null` -- rows with no extracted value are filtered out of
   * `buildReviewFields`'s result, since there is nothing to review). Correction-form pre-fill. */
  rawValue: string;
  /** Human-readable rendering of `rawValue`, for the list/evidence pane. */
  displayValue: string;
  decision: ReviewDecision;
  /** The latest real correction-history entry for this field, present only when `decision === "corrected"`. */
  latestCorrection: CorrectionHistoryEntryBody | null;
  /** `null` today for every field -- no live per-field confidence exists yet; see this module's own
   * header comment. Kept as a real, typed slot (mirrors `contract360ViewModel.ts#FactRow.confidencePct`)
   * so a future task can populate a real 0-100 score with zero change to `fieldTag`/`isFieldBlocking`. */
  confidencePct: number | null;
}

/**
 * Builds one row per correctable field that actually has an extracted value on this contract
 * (AC-1). `history` is `GET /api/contracts/{id}/corrections`'s real, newest-first result, so
 * `history.find(...)` below returns each field's latest correction, matching
 * `ContractCorrectionHistoryQueryService`'s own `OrderByDescending(CorrectedAt)`.
 *
 * `acceptedThisSession` is this screen's own client-only "Accept" acknowledgement
 * (`index.tsx`'s state). There is no accept-only backend call -- `ContractCorrectionService
 * .CorrectAsync` rejects a no-op correction outright ("None of the supplied values differ from the
 * contract's current values"), so an Accept decision cannot be durably recorded the way a Correct
 * decision can (that becomes a real `history` entry via a real value change). An Accept therefore
 * does not survive a reload -- an honest consequence of the real API's shape, not a bug; see
 * `index.tsx`'s own header comment.
 */
export function buildReviewFields(
  contract: Contract360Body,
  history: readonly CorrectionHistoryEntryBody[],
  acceptedThisSession: ReadonlySet<CorrectableFieldName>,
): ReviewFieldRow[] {
  const rows: ReviewFieldRow[] = [];

  for (const def of CORRECTABLE_FIELDS) {
    const rawValue = readCorrectableValue(contract, def.name);
    if (rawValue === null) continue;

    const latestCorrection = history.find((entry) => entry.fieldName === def.name) ?? null;
    const decision: ReviewDecision =
      latestCorrection !== null ? "corrected" : acceptedThisSession.has(def.name) ? "accepted" : "pending";

    rows.push({
      name: def.name,
      label: def.label,
      kind: def.kind,
      required: def.required,
      rawValue,
      displayValue: formatCorrectableValue(def.kind, def.name, rawValue),
      decision,
      latestCorrection,
      confidencePct: null,
    });
  }

  return rows;
}

/**
 * AC-2's tag, generalised for a field's decision state, not only a raw confidence number (see this
 * module's own header comment for why no live number exists yet for this field set). A resolved
 * field (accepted or corrected) always shows a real, honest fact -- never a fabricated percentage.
 * A pending field with a real `confidencePct` reuses `styles/semantics.ts#getConfidenceTag`
 * verbatim (never re-derived) -- the >95/80-95/<80 thresholds are exercised exactly as ADR-019
 * locks them the moment a real score exists. A pending field with no score gets a plain,
 * text-labelled "Needs review" -- the same `.tag-outline` variant a genuine <80% score would carry
 * (ADR-019: outline blocks consequential use), just without inventing the percentage.
 */
export function fieldTag(row: Pick<ReviewFieldRow, "decision" | "confidencePct">): SemanticTag {
  if (row.decision === "corrected") return { variant: "neutral", label: "Corrected" };
  if (row.decision === "accepted") return { variant: "neutral", label: "Accepted by you" };
  if (row.confidencePct !== null) return getConfidenceTag(row.confidencePct);
  return { variant: "outline", label: "Needs review" };
}

/**
 * AC-4's per-field gate predicate. A resolved field never blocks, regardless of confidence -- a
 * human decision (accept or correct) outranks a model confidence estimate (product spec Appendix
 * C). A pending field blocks when it is either a real, confirmed <80% score, or -- today, always --
 * when no score exists at all: the conservative default this module's header comment names, so
 * "disabled until all <80% fields decided" (parent story AC-4, verbatim) stays true even though no
 * field yet carries a real percentage.
 */
export function isFieldBlocking(row: Pick<ReviewFieldRow, "decision" | "confidencePct">): boolean {
  if (row.decision !== "pending") return false;
  return row.confidencePct === null ? true : isConfidenceBlocking(row.confidencePct);
}

export interface ReviewProgress {
  total: number;
  resolvedCount: number;
  blockingCount: number;
}

export function computeReviewProgress(rows: readonly ReviewFieldRow[]): ReviewProgress {
  return {
    total: rows.length,
    resolvedCount: rows.filter((row) => row.decision !== "pending").length,
    blockingCount: rows.filter(isFieldBlocking).length,
  };
}

/** AC-4: "Mark as validated" stays disabled until every blocking field has a decision. */
export function isValidationBlocked(progress: ReviewProgress): boolean {
  return progress.blockingCount > 0;
}

/** AC-4's "visible reason" text (ADR-019 accessibility baseline: "a visible reason, not a hidden
 * control") -- empty string when nothing blocks, so a caller can render it conditionally. */
export function blockedReason(progress: ReviewProgress): string {
  if (progress.blockingCount === 0) return "";
  const isSingular = progress.blockingCount === 1;
  return `${progress.blockingCount} field${isSingular ? "" : "s"} still need${isSingular ? "s" : ""} review before this contract can be marked validated.`;
}
