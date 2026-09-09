/**
 * "Validated" for a `Contract.Status` string (ADR-024 `kbReady`: "at least one document reached
 * `completed`"). There is no dedicated validated-contracts endpoint or count field in this backend,
 * so the one shared signal the web has is `GET /api/contracts`'s own `status` -- free text
 * (`../contracts/portfolioTableFormatters.ts#getPortfolioStatusTag`'s own doc comment has the full
 * provenance: the bootstrap default is the literal `processing`, otherwise whatever the metadata
 * extraction stage last wrote or a human corrected). A contract is treated as validated once it is
 * past every transient/blocking status that field can report: not empty, not `processing`, not
 * `failed`, and nothing containing `review` (requirements.md R-CMP-03 names `needs_review` as the one
 * other not-yet-validated state, in whatever casing/spacing it arrives).
 *
 * One module, used by the shell's `useValidatedContractCount` (rail tier + Ask bar) and by the
 * Portfolio screen (validated rows only, its own reroute state) -- the two used to carry duplicated
 * copies of this predicate under a task file-scope boundary that no longer applies; a single
 * definition means the rail can never light up for a contract the Portfolio then hides, or vice versa.
 */
export function isValidatedContractStatus(status: string): boolean {
  const normalized = status.trim().toLowerCase();
  if (normalized === "" || normalized === "processing" || normalized === "failed") return false;
  return !normalized.includes("review");
}
