/**
 * "Validated" for a `Contract.Status` string -- free text
 * (`../contracts/portfolioTableFormatters.ts#getPortfolioStatusTag`'s own doc comment has the full
 * provenance: the bootstrap default is the literal `processing`, otherwise whatever the metadata
 * extraction stage last wrote or a human corrected). A contract is treated as validated once it is
 * past every transient/blocking status that field can report: not empty, not `processing`, not
 * `failed`, and nothing containing `review` (requirements.md R-CMP-03 names `needs_review` as the one
 * other not-yet-validated state, in whatever casing/spacing it arrives).
 *
 * Task E14/F03/US02/T01 (wave w14 "workspace is real"; ADR-012/ADR-026 w14 footers): **this
 * predicate is no longer a count definition.** `GET /api/workspaces`'s `contractCount` field is now
 * the one server-computed definition of "validated" (ADR-026 §D2: at least one linked document is
 * `Completed`, counted with a real `CountAsync`, not materialised and measured client-side), and
 * `useValidatedContractCount.ts` reads that field instead of filtering with this function -- see
 * that hook's own updated header comment. This function survives only as a row-level display/filter
 * helper: `portfolioViewModel.ts:38` still needs it to decide which Portfolio rows are "validated
 * rows only" for that screen's own reroute state, a screen this wave does not touch. **The two must
 * never both produce a number** -- this one no longer does.
 */
export function isValidatedContractStatus(status: string): boolean {
  const normalized = status.trim().toLowerCase();
  if (normalized === "" || normalized === "processing" || normalized === "failed") return false;
  return !normalized.includes("review");
}

/** Linked document still uploading or being analyzed -- not OK to open yet. */
export function isDocumentPending(documentProcessingStatus: string | null | undefined): boolean {
  if (!documentProcessingStatus) return false;
  const s = documentProcessingStatus.toLowerCase();
  return s === "uploaded" || s === "processing";
}

/** Linked document is parked in human validation. */
export function isDocumentNeedsReview(documentProcessingStatus: string | null | undefined): boolean {
  return documentProcessingStatus?.toLowerCase() === "needsreview";
}

/**
 * Already analyzed / validated / OK to use -- the default Ready bucket on Portfolio and Renewals.
 * A row is ready only when contract status is past every transient/blocking value *and* the linked
 * document is not still uploading, processing, or parked in NeedsReview. Date-determination
 * (`Determined` vs `CannotDetermine`) is not this predicate: a contract with dates can still be
 * in review, and a validated contract without dates is still OK to open.
 */
export function isContractReadyToUse(
  contractStatus: string,
  documentProcessingStatus?: string | null,
): boolean {
  return (
    isValidatedContractStatus(contractStatus) &&
    !isDocumentPending(documentProcessingStatus) &&
    !isDocumentNeedsReview(documentProcessingStatus)
  );
}
