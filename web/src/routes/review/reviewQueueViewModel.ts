import type { PortfolioListItem } from "../../api/client";
import type { TrackedDocument } from "../documents/documentStore";
import { computeAttentionRow } from "../contracts/portfolioAttention";
import { formatSupplier, getContractTypeLabel, getPortfolioStatusTag } from "../contracts/portfolioTableFormatters";
import { getStatusTag, type SemanticTag } from "../../styles/semantics";

export interface ReviewQueueRow {
  /** Stable React key. Portfolio rows use contractId; unlinked uploads use document id. */
  id: string;
  contractId: string | null;
  label: string;
  supplierLabel: string;
  supplierTitle: string | undefined;
  attention: string;
  statusTag: SemanticTag;
}

/**
 * Review-queue landing rows (route `/review`). Portfolio contracts whose status contains
 * "review" (same signal `portfolioAttention.ts` already uses) plus this-session uploads still
 * in `NeedsReview` that are not already represented by a portfolio row. Unlinked uploads stay
 * on the list with no review-detail href -- the same honest gap DocumentStatusTable already
 * shows ("Not yet linked to a contract").
 */
export function buildReviewQueue(
  portfolioItems: readonly PortfolioListItem[],
  trackedDocuments: readonly TrackedDocument[],
  now: Date = new Date(),
): ReviewQueueRow[] {
  const fromPortfolio: ReviewQueueRow[] = [];
  const portfolioContractIds = new Set<string>();

  for (const item of portfolioItems) {
    const attention = computeAttentionRow(item, now);
    if (!attention.isNeedsReview) continue;
    portfolioContractIds.add(item.contractId);
    const supplier = formatSupplier(item.supplierId);
    fromPortfolio.push({
      id: item.contractId,
      contractId: item.contractId,
      label: getContractTypeLabel(item.type),
      supplierLabel: supplier.label,
      supplierTitle: supplier.title,
      attention: attention.issue,
      statusTag: getPortfolioStatusTag(item.status),
    });
  }

  const fromUploads: ReviewQueueRow[] = [];
  for (const document of trackedDocuments) {
    if (document.processingStatus !== "NeedsReview") continue;
    if (document.contractId !== null && portfolioContractIds.has(document.contractId)) continue;
    fromUploads.push({
      id: document.id,
      contractId: document.contractId,
      label: document.fileName,
      supplierLabel: "Not yet available",
      supplierTitle: undefined,
      attention: "Needs review",
      statusTag: getStatusTag("needs_review"),
    });
  }

  return [...fromPortfolio, ...fromUploads];
}
