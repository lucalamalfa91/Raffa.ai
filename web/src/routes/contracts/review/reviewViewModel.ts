import type {
  Contract360Body,
  Contract360DocumentBody,
  ContractFieldEvidenceBody,
  CorrectionHistoryEntryBody,
  PortfolioContractType,
} from "../../../api/client";
import { getConfidenceTag, isConfidenceBlocking, type SemanticTag } from "../../../styles/semantics";
import { formatDateOnly, getContractTypeLabel } from "../portfolioTableFormatters";

/**
 * Pure view-model helpers for the Review / correction screen (route `/contracts/:contractId/review`
 * and the `/documents?review=<documentId>` state, ADR-018; ADR-020 screen 6; task E07/F03/US01/T01,
 * us-01-field-review-correction AC-1/AC-2/AC-3/AC-4). Same one-concern-per-file split
 * `../contract360/contract360ViewModel.ts` already established for this folder: no React here, so
 * every rule below is unit-testable without rendering anything.
 *
 * **Field catalogue mirrors the backend exactly, on purpose.** `CORRECTABLE_FIELDS` below is a
 * client-side copy of `ContractCorrectionService.CorrectableFieldNames` (backend/src/
 * Raffa.Documents.Contracts/Application/ContractCorrectionService.cs) -- the only field names
 * `PATCH /api/contracts/{id}` (`correctContract`) will ever accept, including `supplier` (requirements
 * R-SUP-03: "from the review UI where a user names the supplier as a correction"), which is a
 * supplier *name* the backend resolves into a link, never a guid.
 *
 * **Confidence and source are real now.** `GET /api/contracts/{id}/evidence` (`getContractEvidence`)
 * exposes the `ExtractionEvidence` trail the pipeline has always written -- per field: the value the
 * model proposed, its 0..1 confidence, the page, the quoted span, the passage around it and the
 * model id. `buildReviewFields` folds the latest row per field into each `ReviewFieldRow`, so
 * `fieldTag`/`isFieldBlocking` apply spec §7.3's thresholds (`styles/semantics.ts#getConfidenceTag`,
 * >95 / 80-95 / <80) to a real score. A field with no evidence at all (a bootstrap placeholder the
 * extraction never touched, or a document processed before evidence was recorded) keeps the
 * conservative "Needs review" posture: no number is invented (Appendix C rule 10), and the field
 * blocks until a human decides.
 *
 * **A proposal the pipeline did not apply is still reviewable.** The `supplier` fact is critical
 * (spec §7.3): below the 0.8 bar the backend records the evidence but refuses to link the supplier,
 * so the contract carries no supplier name while the evidence carries a proposed one. Such a row is
 * built from the proposal (`proposalPending: true`) and "Accept" has to *write* it -- see
 * `useReviewSession.ts` -- because accepting an unapplied proposal is a real change, unlike accepting
 * a value the contract already holds.
 */

export type CorrectableFieldName =
  | "type"
  | "supplier"
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
   * the backend rejects a null/empty value for a required field outright (and refuses to clear
   * `supplier`), so sending `null` for one would only trade an honest "cannot be cleared" 400 for a
   * less obviously-related one. */
  required: boolean;
}

/** Order mirrors `ContractCorrectionService.CorrectableFields`' own declaration order, with
 * `supplier` (its one non-scalar, name-resolved field) right after the type. */
export const CORRECTABLE_FIELDS: readonly FieldDefinition[] = [
  { name: "type", label: "Contract type", kind: "enum", required: true },
  { name: "supplier", label: "Supplier", kind: "text", required: true },
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
 * correctable field's current value). `null` when this optional field has never been extracted
 * (for `supplier`: no supplier is linked). Mirrors `ContractCorrectionService`'s own per-field
 * `Read` table exactly (same canonical string shapes this screen must also send back: dates
 * `yyyy-MM-dd`, decimals/ints via plain `String()`, bool as `"true"`/`"false"`).
 */
export function readCorrectableValue(contract: Contract360Body, name: CorrectableFieldName): string | null {
  const { header, tabs } = contract;
  switch (name) {
    case "type":
      return header.type;
    case "supplier":
      return header.supplierName;
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
    case "int": {
      const numeric = Number(rawValue);
      return Number.isFinite(numeric) ? new Intl.NumberFormat("en-GB").format(numeric) : rawValue;
    }
    case "bool":
      return rawValue === "true" ? "Yes" : "No";
    case "enum":
      return name === "type" ? getContractTypeLabel(rawValue as PortfolioContractType) : rawValue;
    default:
      return rawValue;
  }
}

export type ReviewDecision = "pending" | "accepted" | "corrected";

/** Latest evidence per field, keyed by the backend's `fieldName` lower-cased (the backend compares
 * field names case-insensitively, the review catalogue uses camelCase). */
export type EvidenceByField = ReadonlyMap<string, ContractFieldEvidenceBody>;

export function indexEvidence(evidence: readonly ContractFieldEvidenceBody[]): EvidenceByField {
  const byField = new Map<string, ContractFieldEvidenceBody>();
  for (const entry of evidence) {
    byField.set(entry.fieldName.toLowerCase(), entry);
  }
  return byField;
}

export interface ReviewFieldRow {
  name: CorrectableFieldName;
  label: string;
  kind: FieldKind;
  /** See `FieldDefinition.required`'s own doc comment. */
  required: boolean;
  /** Canonical wire-format value (never `null` -- rows with neither a contract value nor an
   * extraction proposal are filtered out of `buildReviewFields`'s result, since there is nothing
   * to review). The contract's current value, or -- when the contract has none -- the value the
   * extraction proposed (`proposalPending`). Correction-form pre-fill. */
  rawValue: string;
  /** Human-readable rendering of `rawValue`, for the list/evidence pane. */
  displayValue: string;
  decision: ReviewDecision;
  /** The latest real correction-history entry for this field, present only when `decision === "corrected"`. */
  latestCorrection: CorrectionHistoryEntryBody | null;
  /** The extraction's real 0-100 score for this field, from `GET /api/contracts/{id}/evidence`;
   * `null` when no evidence row exists for it (nothing is invented -- see the module header). */
  confidencePct: number | null;
  /** The latest evidence row behind `confidencePct`: page, span, passage, model. */
  evidence: ContractFieldEvidenceBody | null;
  /** True when the extraction proposed a value the contract does not carry (today: a `supplier`
   * fact below the critical bar the pipeline declined to link). Accepting such a row is a real
   * write (`correctContract` with the proposed value), not a client-side acknowledgement. */
  proposalPending: boolean;
}

/** The canonical wire string for a proposed value, so a `proposalPending` row's Accept sends exactly
 * what `ContractCorrectionService` expects for the field's kind. */
function normalizeProposedValue(kind: FieldKind, proposed: string): string {
  if (kind === "bool") {
    return proposed.trim().toLowerCase() === "true" ? "true" : "false";
  }
  return proposed.trim();
}

/**
 * Builds one row per correctable field that has either a value on this contract or an extraction
 * proposal in `evidence` (AC-1). `history` is `GET /api/contracts/{id}/corrections`'s real,
 * newest-first result, so `history.find(...)` below returns each field's latest correction, matching
 * `ContractCorrectionHistoryQueryService`'s own `OrderByDescending(CorrectedAt)`.
 *
 * `acceptedThisSession` is this screen's own client-side "Accept" acknowledgement for a field whose
 * value the contract already holds -- there is no accept-only backend call for an unchanged value
 * (`ContractCorrectionService.CorrectAsync` rejects a no-op correction), so the durable record of
 * those acceptances is the `acceptedFields` list "Mark as validated" sends to
 * `POST /api/documents/{id}/validate`, which the backend writes onto the `document.validated` audit
 * row. A reload before that click re-asks any field that was only Accepted, never Corrected.
 */
export function buildReviewFields(
  contract: Contract360Body,
  history: readonly CorrectionHistoryEntryBody[],
  acceptedThisSession: ReadonlySet<CorrectableFieldName>,
  evidence: EvidenceByField = new Map(),
): ReviewFieldRow[] {
  const rows: ReviewFieldRow[] = [];

  for (const def of CORRECTABLE_FIELDS) {
    const currentValue = readCorrectableValue(contract, def.name);
    const fieldEvidence = evidence.get(def.name.toLowerCase()) ?? null;
    const proposedValue = fieldEvidence?.value ?? null;

    if (currentValue === null && (proposedValue === null || proposedValue.trim() === "")) continue;

    const proposalPending = currentValue === null && proposedValue !== null;
    const rawValue = currentValue ?? normalizeProposedValue(def.kind, proposedValue!);

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
      confidencePct: fieldEvidence?.confidence == null ? null : fieldEvidence.confidence * 100,
      evidence: fieldEvidence,
      proposalPending,
    });
  }

  return rows;
}

/**
 * AC-2's tag, generalised for a field's decision state. A resolved field (accepted or corrected)
 * always shows a real, honest fact -- never a fabricated percentage. A pending field with a real
 * `confidencePct` reuses `styles/semantics.ts#getConfidenceTag` verbatim (never re-derived) -- the
 * >95/80-95/<80 thresholds exactly as ADR-019 locks them. A pending field with no score gets a
 * plain, text-labelled "Needs review" -- the same `.tag-outline` variant a genuine <80% score would
 * carry (ADR-019: outline blocks consequential use), just without inventing the percentage.
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
 * C). A pending field blocks when it is either a real, confirmed <80% score, or when no score
 * exists at all: the conservative default the module header names.
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

/** The field names "Mark as validated" reports to `POST /api/documents/{id}/validate` as accepted
 * as extracted: every row the reviewer clicked Accept on (corrected rows are already durable). */
export function acceptedFieldNames(rows: readonly ReviewFieldRow[]): string[] {
  return rows.filter((row) => row.decision === "accepted").map((row) => row.name);
}

/**
 * The document a review of `contract` signs off. The Documents route knows it (`?review=<id>`) and
 * passes it as `preferredDocumentId`; the `/contracts/:id/review` route does not, so it falls back
 * to the contract's own documents -- the one still needing review first, else the first one, else
 * `null` (a contract with no document at all has nothing to validate). Returns the id and, when the
 * aggregate lists the document, its current status, so the screen can tell an already-validated
 * document apart from one still to sign off.
 */
export function resolveReviewDocument(
  contract: Contract360Body,
  preferredDocumentId: string | null = null,
): { documentId: string; processingStatus: Contract360DocumentBody["processingStatus"] | null } | null {
  const documents = contract.tabs.documents;

  if (preferredDocumentId !== null) {
    const known = documents.find((document) => document.documentId === preferredDocumentId) ?? null;
    return { documentId: preferredDocumentId, processingStatus: known?.processingStatus ?? null };
  }

  const candidate = documents.find((document) => document.processingStatus === "NeedsReview") ?? documents[0] ?? null;
  return candidate === null ? null : { documentId: candidate.documentId, processingStatus: candidate.processingStatus };
}

/** Where the evidence card's highlighted passage splits: text before the span, the span, text
 * after -- or the whole passage un-highlighted when the offsets do not fit it (never a wrong
 * highlight). `null` when the evidence carries no passage at all. */
export function splitPassage(
  evidence: Pick<ContractFieldEvidenceBody, "passage" | "highlightStart" | "highlightLength">,
): { before: string; highlight: string; after: string } | null {
  if (evidence.passage === null) return null;
  const { passage, highlightStart, highlightLength } = evidence;
  if (
    highlightStart === null ||
    highlightLength === null ||
    highlightStart < 0 ||
    highlightLength <= 0 ||
    highlightStart + highlightLength > passage.length
  ) {
    return { before: passage, highlight: "", after: "" };
  }
  return {
    before: passage.slice(0, highlightStart),
    highlight: passage.slice(highlightStart, highlightStart + highlightLength),
    after: passage.slice(highlightStart + highlightLength),
  };
}
